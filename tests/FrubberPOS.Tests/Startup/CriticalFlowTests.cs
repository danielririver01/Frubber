using Dapper;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Backup;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Logging;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Startup;
using FrubberPOS.Domain.Products;
using FrubberPOS.Infrastructure.Backup;
using FrubberPOS.Infrastructure.Logging;
using FrubberPOS.Infrastructure.Persistence.Users;
using FrubberPOS.Infrastructure.Security;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Startup;

/// <summary>
/// Critical flow on one isolated, freshly migrated database (JHO-56):
/// first start → first administrator → sign-in → product → open cash →
/// sale → void (stock reintegrated, total out of cash) → close cash →
/// late void refused → manual backup verifies and publishes. Everything
/// runs against the real SQLite stack and the file is deleted afterwards.
/// </summary>
public sealed class CriticalFlowTests
{
    [Fact]
    public async Task FirstStart_Sale_Void_Close_AndBackup_EndToEnd()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var workDir = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var logger = SerilogLoggerFactory.Create(new LoggingOptions(Path.Combine(workDir, "logs")));
        try
        {
            var ct = CancellationToken.None;
            var users = new DapperUserRepository();
            var hasher = new Pbkdf2PinHasher();

            // 1) Primer inicio: la base vacía exige el wizard y crea el único admin (sin PIN por defecto).
            var check = new CheckFirstStartUseCase(users, bed.Units);
            Assert.True((await check.ExecuteAsync(new CheckFirstStartRequest(), ct)).Value.IsFirstStart);
            var first = new CreateFirstAdministratorUseCase(users, hasher, bed.Units);
            var admin = await first.ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), ct);
            Assert.True(admin.IsSuccess, admin.Error?.Message);
            Assert.False((await check.ExecuteAsync(new CheckFirstStartRequest(), ct)).Value.IsFirstStart);

            // 2) Acceso con el PIN elegido.
            var session = new FakeCurrentSession();
            var login = new AuthenticateUserUseCase(users, hasher, session, bed.Units);
            Assert.True((await login.ExecuteAsync(new AuthenticateUserRequest(admin.Value.UserId, "1234"), ct)).IsSuccess);

            // 3) Producto con existencia.
            var created = await new CreateProductUseCase(bed.Products, bed.Units, session).ExecuteAsync(
                new CreateProductRequest("M1", "Mango", ProductUnit.Kg, 6_000, 5_000), ct);
            Assert.True(created.IsSuccess, created.Error?.Message);

            // 4) Apertura de caja.
            var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, session, bed.Clock, bed.Gate);
            Assert.True((await open.ExecuteAsync(new OpenCashSessionRequest(50_000), ct)).IsSuccess);

            // 5) Venta en efectivo: inventario descontado y caja refleja el total.
            var finalize = new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, session, bed.Clock, bed.Gate);
            var sold = await finalize.ExecuteAsync(
                new FinalizeSaleRequest(
                    [new DraftSaleLine(created.Value.ProductId, "M1", "Mango", ProductUnit.Kg, 6_000, 1_250)],
                    0, "CASH", 8_000),
                ct);
            Assert.True(sold.IsSuccess, sold.Error?.Message);
            Assert.Equal(1, sold.Value.SaleNumber);
            Assert.Equal(3_750, await StockAsync(bed, created.Value.ProductId));

            // 6) Anulación completa mientras la caja sigue abierta: inventario reintegrado.
            var admin2 = ProductTestBed.AdminSession(); // mismo usuario Dueño (id 1)
            var voidSale = new VoidSaleUseCase(bed.Voids, bed.Sales, bed.Units, admin2, bed.Clock, bed.Gate);
            Assert.True((await voidSale.ExecuteAsync(new VoidSaleRequest(1), ct)).IsSuccess);
            Assert.Equal(5_000, await StockAsync(bed, created.Value.ProductId));

            // 7) Cierre informativo: totales congelados sin la anulada.
            var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, session, bed.Clock, bed.Gate);
            Assert.True((await close.ExecuteAsync(new CloseCashSessionRequest(), ct)).IsSuccess);
            await using (var verify = new SqliteConnection($"Data Source={bed.DbPath}"))
            {
                await verify.OpenAsync(ct);
                var closed = await verify.QuerySingleAsync<(long Net, long Expected)>(
                    "SELECT ClosedNetSalesCop AS Net, ClosedExpectedCashCop AS Expected FROM CashSession WHERE CashSessionId = 1;");
                Assert.Equal(0, closed.Net);
                Assert.Equal(50_000, closed.Expected);
            }

            // 8) Tras el cierre la venta ya no puede anularse ni por atajo (RN-HIS-007).
            var lateVoid = await voidSale.ExecuteAsync(new VoidSaleRequest(1), ct);
            Assert.True(lateVoid.IsFailure);
            Assert.Equal(ErrorCode.Conflict, lateVoid.Error!.Code);

            // 9) Respaldo manual a la ubicación elegida: copia íntegra y original intacta.
            var backup = new CreateBackupUseCase(
                new SqliteBackupService(new DatabaseOptions(bed.DbPath)),
                new SqliteBackupStatusService(new DatabaseOptions(bed.DbPath)),
                session, logger);
            var destination = Path.Combine(workDir, "respaldo.db");
            var backed = await backup.ExecuteAsync(new CreateBackupRequest(destination), ct);
            Assert.True(backed.IsSuccess, backed.Error?.Message);
            Assert.True(File.Exists(destination));
            await using (var copy = new SqliteConnection($"Data Source={destination};Mode=ReadOnly;Pooling=false"))
            {
                await copy.OpenAsync(ct);
                Assert.Equal("ok", await copy.ExecuteScalarAsync<string>("PRAGMA integrity_check;"));
                Assert.Equal(1, await copy.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Sale;"));
                Assert.Equal("VOIDED", await copy.ExecuteScalarAsync<string>(
                    "SELECT SaleStatus FROM V_SaleHistory WHERE SaleNumber = 1;"));
            }
        }
        finally
        {
            logger.Dispose();
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }

    private static async Task<long> StockAsync(ProductTestBed bed, int productId)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT StockMilli FROM Product WHERE ProductId = @Id;", new { Id = productId });
    }
}
