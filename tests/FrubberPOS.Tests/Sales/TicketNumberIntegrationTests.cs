using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Sales;
using FrubberPOS.Domain.Products;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Cash;
using FrubberPOS.Infrastructure.Persistence.Sales;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;
using Npgsql;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// JHO-111: regresión del camino de cobro actual. Cada prueba usa una base
/// desechable independiente con las migraciones embebidas de la aplicación.
/// No prueba puestos 2.0 ni el esquema database/postgresql/001_Baseline.sql.
/// </summary>
public sealed class TicketNumberIntegrationTests
{
    private static async Task SeedAsync(ScratchDatabase db)
    {
        var migrated = await new MigrationRunner().MigrateAsync(db.ConnectionString, CancellationToken.None);
        Assert.True(migrated.IsSuccess, migrated.Error?.Message);

        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO "User" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)
            VALUES (1, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);
            INSERT INTO CashSession (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)
            VALUES (1, 0, 1, 'Admin', 0);
            INSERT INTO Product (ProductId, Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)
            VALUES (1, 'T1', 'Producto de prueba', 'UNIT', 1500, 1000000, 1, 1);
            """, new { Salt = new byte[16], Hash = new byte[32] });
    }

    private static FinalizeSaleUseCase CreateUseCase(ScratchDatabase db)
    {
        var units = new NpgsqlUnitOfWorkFactory(new NpgsqlConnectionFactory(new DatabaseOptions(db.ConnectionString)));
        return new FinalizeSaleUseCase(
            new DapperSaleRepository(), new DapperCashSessionRepository(), units,
            ProductTestBed.AdminSession(), new FakeClock(DateTimeOffset.UtcNow), new FakeOperationalGate());
    }

    private static FinalizeSaleRequest Request() => new(
        [new DraftSaleLine(1, "T1", "Producto de prueba", ProductUnit.Unit, 1500, 1000)],
        0, "CASH", 1500);

    [Fact]
    public async Task ConcurrentCheckout_500Sales_ProducesConsecutiveNumbersAndCompleteSales()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        await SeedAsync(db);

        const int workers = 10;
        const int salesPerWorker = 50;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        // Cada trabajador tiene su caso de uso; cada venta abre una unidad
        // de trabajo con su propia conexión durante la transacción.
        // La puerta de inicio hace que los diez se lancen al mismo tiempo.
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<List<long>> SellAsync()
        {
            var useCase = CreateUseCase(db);
            var numbers = new List<long>();
            await start.Task;
            for (var i = 0; i < salesPerWorker; i++)
            {
                var result = await useCase.ExecuteAsync(Request(), timeout.Token);
                Assert.True(result.IsSuccess, result.Error?.Message);
                numbers.Add(result.Value.SaleNumber);
            }
            return numbers;
        }

        var tasks = Enumerable.Range(0, workers).Select(_ => SellAsync()).ToArray();
        start.SetResult(true);
        var results = await Task.WhenAll(tasks);
        var numbers = results.SelectMany(x => x).OrderBy(x => x).ToArray();
        Assert.Equal(Enumerable.Range(1, 500).Select(x => (long)x).ToArray(), numbers);

        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        Assert.Equal(500L, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Sale;"));
        Assert.Equal(500L, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM SaleLine;"));
        Assert.Equal(500L, await connection.ExecuteScalarAsync<long>("SELECT LastValue FROM SystemSequence WHERE SequenceName = 'SaleNumber';"));
        Assert.Equal(500000L, await connection.ExecuteScalarAsync<long>("SELECT StockMilli FROM Product WHERE ProductId = 1;"));
    }

    [Fact]
    public async Task CheckoutFailureAfterNumberAndHeader_RollsBackAndReusesNumber()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        await SeedAsync(db);

        // Falla en la línea después de guardar número y cabecera; la función
        // comprueba que ambos existan antes de provocar el error de prueba.
        await using (var setup = db.NewConnection())
        {
            await setup.OpenAsync();
            await setup.ExecuteAsync("""
                CREATE FUNCTION jho111_fail_line() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (SELECT LastValue FROM SystemSequence WHERE SequenceName = 'SaleNumber') <> 1
                     OR NOT EXISTS (SELECT FROM Sale WHERE SaleId = NEW.SaleId AND SaleNumber = 1) THEN
                    RAISE EXCEPTION 'JHO111 fallo antes de cabecera';
                  END IF;
                  RAISE EXCEPTION 'JHO111 fallo simulado despues de cabecera' USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER jho111_fail_line BEFORE INSERT ON SaleLine
                FOR EACH ROW EXECUTE FUNCTION jho111_fail_line();
                """);
        }

        var useCase = CreateUseCase(db);
        var error = await Assert.ThrowsAsync<PostgresException>(() => useCase.ExecuteAsync(Request(), CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Contains("JHO111 fallo simulado despues de cabecera", error.MessageText);

        await using (var verify = db.NewConnection())
        {
            await verify.OpenAsync();
            Assert.Equal(0L, await verify.ExecuteScalarAsync<long>("SELECT LastValue FROM SystemSequence WHERE SequenceName = 'SaleNumber';"));
            Assert.Equal(0L, await verify.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Sale;"));
            Assert.Equal(0L, await verify.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM SaleLine;"));
            Assert.Equal(1000000L, await verify.ExecuteScalarAsync<long>("SELECT StockMilli FROM Product WHERE ProductId = 1;"));
            await verify.ExecuteAsync("DROP TRIGGER jho111_fail_line ON SaleLine; DROP FUNCTION jho111_fail_line();");
        }

        var success = await useCase.ExecuteAsync(Request(), CancellationToken.None);
        Assert.True(success.IsSuccess, success.Error?.Message);
        Assert.Equal(1L, success.Value.SaleNumber);
        await using var final = db.NewConnection();
        await final.OpenAsync();
        Assert.Equal(1L, await final.ExecuteScalarAsync<long>("SELECT LastValue FROM SystemSequence WHERE SequenceName = 'SaleNumber';"));
        Assert.Equal(1L, await final.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Sale;"));
        Assert.Equal(1L, await final.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM SaleLine;"));
    }
}
