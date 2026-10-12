using Dapper;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Infrastructure.Schema;

/// <summary>
/// Schema enforcement on a freshly migrated, isolated database (JHO-56):
/// the DDL is the last line of defense (§6.3), so these tests attack the
/// base directly with raw SQL — foreign keys, CHECKs, unique indexes and
/// immutability triggers must reject bad writes even when no use case
/// stands in front. Each test deletes its database afterwards.
/// </summary>
public sealed class SchemaEnforcementTests : IAsyncDisposable
{
    private readonly List<string> paths = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var path in paths)
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
            foreach (var file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }

        await Task.CompletedTask;
    }

    private async Task<SqliteConnection> FreshDbAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");
        paths.Add(path);
        var migrated = await new MigrationRunner().MigrateAsync($"Data Source={path}", CancellationToken.None);
        Assert.True(migrated.IsSuccess, migrated.Error?.Message);

        var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
        return connection;
    }

    private static async Task SeedUserAsync(SqliteConnection db, int id, string role, int active = 1)
    {
        await db.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@Id, @Name, @Role, @Active, @Salt, @Hash, 1, 1);",
            new { Id = id, Name = $"Usuario {id}", Role = role, Active = active, Salt = new byte[16], Hash = new byte[32] });
    }

    private static async Task SeedOpenCashAsync(SqliteConnection db, int id = 1, int userId = 1)
    {
        await db.ExecuteAsync(
            "INSERT INTO CashSession (CashSessionId, OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (@Id, 1, 0, @UserId, 'Usuario 1', 0);",
            new { Id = id, UserId = userId });
    }

    private static async Task SeedProductAsync(SqliteConnection db, int id = 1, string plu = "P1", int stockMilli = 0)
    {
        await db.ExecuteAsync(
            "INSERT INTO Product (ProductId, Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)"
            + " VALUES (@Id, @Plu, 'Producto', 'KG', 1000, @Stock, 1, @Locked);",
            new { Id = id, Plu = plu, Stock = stockMilli, Locked = stockMilli == 0 ? 0 : 1 });
    }

    private static async Task SeedSaleAsync(SqliteConnection db, int id = 1, int sessionId = 1, int userId = 1)
    {
        await db.ExecuteAsync(
            "INSERT INTO Sale (SaleId, SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
            + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (@Id, @Id, @SessionId, @UserId, 'Usuario 1', 2, 0, 1000, 0, 1000, 'CASH', 1000, 0);",
            new { Id = id, SessionId = sessionId, UserId = userId });
    }

    [Fact]
    public async Task ForeignKeys_AreEnabledOnEveryFactoryConnection()
    {
        var path = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");
        paths.Add(path);
        var factory = new SqliteConnectionFactory(new FrubberPOS.Application.Persistence.DatabaseOptions(path));

        await using var connection = await factory.CreateOpenConnectionAsync(CancellationToken.None);

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("PRAGMA foreign_keys;"));
    }

    [Fact]
    public async Task ForeignKeys_RejectOrphanRows()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedProductAsync(db, 1);

        // SaleLine sin venta y venta sin caja deben fallar (FKs reales activadas).
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO SaleLine (SaleId, LineNumber, ProductId, PluSnapshot, ProductNameSnapshot, UnitSnapshot, UnitPriceCopSnapshot, QuantityMilli, LineTotalCop)"
            + " VALUES (999, 1, 1, 'P1', 'Producto', 'KG', 1000, 1000, 1000);"));
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot, SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (1, 777, 1, 'Usuario 1', 2, 0, 1000, 0, 1000, 'CASH', 1000, 0);"));
    }

    [Fact]
    public async Task Plu_UniqueIndex_IsCaseInsensitive()
    {
        await using var db = await FreshDbAsync();
        await SeedProductAsync(db, 1, "M1");

        // UX_Product_Plu_NoCase respalda la unicidad (RN-PRO-003); el CHECK
        // (Plu = upper(Plu)) garantiza que lo guardado ya viene normalizado,
        // así que el duplicado exacto rebota por el índice.
        var ex = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Product (Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)"
            + " VALUES ('M1', 'Otro', 'KG', 1000, 0, 1, 0);"));
        Assert.Contains("UNIQUE", ex.Message);

        // Y un PLU en minúsculas ni siquiera pasa el CHECK de normalización (RN-PRO-002).
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Product (Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)"
            + " VALUES ('m2', 'Otro más', 'KG', 1000, 0, 1, 0);"));
    }

    [Fact]
    public async Task OnlyOneCashSession_CanStayOpen()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedOpenCashAsync(db, 1);

        // UX_CashSession_OnlyOneOpen: la segunda caja abierta no cabe (RN-CAJ-001).
        var ex = await Assert.ThrowsAsync<SqliteException>(() => SeedOpenCashAsync(db, 2));
        Assert.Contains("UNIQUE", ex.Message);
    }

    [Fact]
    public async Task Sale_IsImmutable_NoUpdateNoDelete()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedOpenCashAsync(db);
        await SeedSaleAsync(db);

        var update = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "UPDATE Sale SET TotalCop = 1 WHERE SaleId = 1;"));
        Assert.Contains("Sale es inmutable", update.Message); // PR-001 a nivel motor
        var delete = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "DELETE FROM Sale WHERE SaleId = 1;"));
        Assert.Contains("Sale no puede eliminarse", delete.Message);
    }

    [Fact]
    public async Task SaleLine_IsImmutable_NoUpdateNoDelete()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedOpenCashAsync(db);
        await SeedProductAsync(db, 1);
        await SeedSaleAsync(db);
        await db.ExecuteAsync(
            "INSERT INTO SaleLine (SaleId, LineNumber, ProductId, PluSnapshot, ProductNameSnapshot, UnitSnapshot, UnitPriceCopSnapshot, QuantityMilli, LineTotalCop)"
            + " VALUES (1, 1, 1, 'P1', 'Producto', 'KG', 1000, 1000, 1000);");

        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "UPDATE SaleLine SET QuantityMilli = 2000 WHERE SaleId = 1;"));
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "DELETE FROM SaleLine WHERE SaleId = 1;"));
    }

    [Fact]
    public async Task SaleVoid_RequiresOpenCashAndActiveAdmin_AndIsImmutable()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedUserAsync(db, 2, "CASHIER");
        await SeedOpenCashAsync(db);
        await SeedSaleAsync(db);

        // La anulación exige administrador activo: un cajero no puede anular a nivel motor.
        var cashier = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
            + " VALUES (1, 3, 0, 2, 'Usuario 2');"));
        Assert.Contains("administrador activo", cashier.Message);

        // Caja cerrada: la misma venta ya no puede anularse (RN-HIS-007 a nivel motor).
        await db.ExecuteAsync(
            "UPDATE CashSession SET ClosedAtUtcMs = 4, ClosedAtOffsetMinutes = 0, ClosedByUserId = 1, ClosedByNameSnapshot = 'Usuario 1',"
            + " ClosedCashSalesCop = 0, ClosedTransferSalesCop = 0, ClosedDiscountCop = 0, ClosedNetSalesCop = 0,"
            + " ClosedExpectedCashCop = 0, ClosedSaleCount = 1 WHERE CashSessionId = 1;");
        var closed = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
            + " VALUES (1, 5, 0, 1, 'Usuario 1');"));
        Assert.Contains("caja abierta", closed.Message);

        // Ojo: caja cerrada arriba quedó con SaleCount = 1 pero la venta sigue ACTIVA;
        // el CHECK de totales se valida aparte. Reabrimos para probar inmutabilidad del void.
        await db.ExecuteAsync(
            "INSERT INTO CashSession (CashSessionId, OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (2, 6, 0, 1, 'Usuario 1', 0);");
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
            + " VALUES (1, 7, 0, 1, 'Usuario 1');")); // su caja (1) sigue cerrada
    }

    [Fact]
    public async Task VoidRow_IsImmutable_NoUpdateNoDelete()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedOpenCashAsync(db);
        await SeedProductAsync(db, 1);
        await SeedSaleAsync(db);
        await db.ExecuteAsync(
            "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
            + " VALUES (1, 3, 0, 1, 'Usuario 1');");

        var update = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "UPDATE SaleVoid SET AdminUserId = 1 WHERE SaleId = 1;"));
        Assert.Contains("SaleVoid es inmutable", update.Message);
        var delete = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "DELETE FROM SaleVoid WHERE SaleId = 1;"));
        Assert.Contains("SaleVoid no puede eliminarse", delete.Message);
    }

    [Fact]
    public async Task ClosedCashSession_IsImmutable_AndOpeningFieldsNeverChange()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedOpenCashAsync(db);
        await SeedProductAsync(db, 1);
        await SeedSaleAsync(db); // con venta, ni siquiera el saldo inicial se toca (003)

        // Los datos de apertura son inmutables (001); con ventas registradas el
        // saldo inicial tampoco puede corregirse (003 / JHO-61).
        var opening = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "UPDATE CashSession SET OpenedAtUtcMs = 9 WHERE CashSessionId = 1;"));
        Assert.Contains("apertura de caja son inmutables", opening.Message);
        var balance = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "UPDATE CashSession SET OpeningBalanceCop = 999 WHERE CashSessionId = 1;"));
        Assert.Contains("apertura de caja son inmutables", balance.Message);

        await db.ExecuteAsync(
            "UPDATE CashSession SET ClosedAtUtcMs = 4, ClosedAtOffsetMinutes = 0, ClosedByUserId = 1, ClosedByNameSnapshot = 'Usuario 1',"
            + " ClosedCashSalesCop = 0, ClosedTransferSalesCop = 0, ClosedDiscountCop = 0, ClosedNetSalesCop = 0,"
            + " ClosedExpectedCashCop = 0, ClosedSaleCount = 1 WHERE CashSessionId = 1;");

        // Una caja cerrada no se recalcula jamás (PR-006 / RN-CAJ-010 a nivel motor).
        var closed = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "UPDATE CashSession SET ClosedSaleCount = 5 WHERE CashSessionId = 1;"));
        Assert.Contains("caja cerrada es inmutable", closed.Message);
    }

    [Fact]
    public async Task Product_UnitChangeAndUnlock_AreAbortedWhenLocked()
    {
        await using var db = await FreshDbAsync();
        await SeedProductAsync(db, 1, "K1", stockMilli: 5_000); // existencia > 0 bloquea la unidad

        var change = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "UPDATE Product SET Unit = 'LB' WHERE ProductId = 1;"));
        Assert.Contains("bloqueada", change.Message); // RN-PRO-008 a nivel motor

        var unlock = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "UPDATE Product SET UnitLocked = 0 WHERE ProductId = 1;"));
        Assert.Contains("irreversible", unlock.Message);
    }

    [Fact]
    public async Task Sale_ArithmeticAndPaymentChecks_EnforcedByDdl()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedOpenCashAsync(db);

        // Total inconsistente con subtotal - descuento.
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot, SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (1, 1, 1, 'Usuario 1', 2, 0, 1000, 100, 950, 'CASH', 950, 0);"));
        // Efectivo insuficiente (RN-VEN-011 a nivel motor).
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot, SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (1, 1, 1, 'Usuario 1', 2, 0, 1000, 0, 1000, 'CASH', 500, 0);"));
        // Descuento mayor que el subtotal (RN-VEN-008 a nivel motor).
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot, SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (1, 1, 1, 'Usuario 1', 2, 0, 1000, 1001, 0, 'TRANSFER', NULL, NULL);"));
        // Descuento negativo (RN-VEN-008, también por debajo del caso de uso).
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot, SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (1, 1, 1, 'Usuario 1', 2, 0, 1000, -1, 1001, 'CASH', 1001, 0);"));
        // Transferencia con recibido (RN-VEN-012 a nivel motor).
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot, SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (1, 1, 1, 'Usuario 1', 2, 0, 1000, 0, 1000, 'TRANSFER', 1000, 0);"));
    }

    [Fact]
    public async Task Product_AndSaleLine_UnitChecks_EnforcedByDdl()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedOpenCashAsync(db);

        // UNIT con existencia fraccionaria (RN-UNI-003 a nivel motor).
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Product (Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)"
            + " VALUES ('U1', 'Limón', 'UNIT', 500, 1500, 1, 1);"));
        // Precio cero (RN-PRO-005 a nivel motor).
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Product (Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)"
            + " VALUES ('U2', 'Lulo', 'KG', 0, 0, 1, 0);"));
        // PLU en minúsculas viola la normalización guardada (RN-PRO-002 a nivel motor).
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO Product (Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)"
            + " VALUES ('m1', 'Mango', 'KG', 100, 0, 1, 0);"));

        await SeedProductAsync(db, 1);
        await SeedSaleAsync(db);
        // Línea por unidad con cantidad fraccionaria y línea con importe mal redondeado.
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO SaleLine (SaleId, LineNumber, ProductId, PluSnapshot, ProductNameSnapshot, UnitSnapshot, UnitPriceCopSnapshot, QuantityMilli, LineTotalCop)"
            + " VALUES (1, 1, 1, 'P1', 'Producto', 'UNIT', 500, 1500, 750);"));
        await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteAsync(
            "INSERT INTO SaleLine (SaleId, LineNumber, ProductId, PluSnapshot, ProductNameSnapshot, UnitSnapshot, UnitPriceCopSnapshot, QuantityMilli, LineTotalCop)"
            + " VALUES (1, 1, 1, 'P1', 'Producto', 'KG', 1000, 1000, 999);")); // debía ser 1000
    }

    [Fact]
    public async Task HotPathIndexes_Exist()
    {
        await using var db = await FreshDbAsync();

        var names = (await db.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type IN ('index', 'trigger');")).ToHashSet(StringComparer.Ordinal);

        // Índices que sostienen RN-OPE-005 (búsqueda e historial con volumen).
        foreach (var expected in new[]
        {
            "UX_Product_Plu_NoCase", "IX_Product_Active_Name", "UX_CashSession_OnlyOneOpen",
            "UX_Sale_SaleNumber", "IX_Sale_SoldAt", "IX_Sale_CashSession_SoldAt",
            "IX_Sale_Cashier_SoldAt", "UX_SaleLine_Sale_LineNumber", "IX_SaleLine_Product",
            "IX_SaleVoid_VoidedAt",
            "TR_Product_BlockUnitChange", "TR_Product_PreventUnitUnlock",
            "TR_CashSession_ProtectOpeningFields", "TR_CashSession_NoUpdateAfterClose",
            "TR_Sale_RequireOpenCashAndActiveUser", "TR_Sale_NoUpdate", "TR_Sale_NoDelete",
            "TR_SaleLine_NoUpdate", "TR_SaleLine_NoDelete",
            "TR_SaleVoid_RequireOpenCashAndAdmin", "TR_SaleVoid_NoUpdate", "TR_SaleVoid_NoDelete",
        })
        {
            Assert.Contains(expected, names);
        }
    }
}
