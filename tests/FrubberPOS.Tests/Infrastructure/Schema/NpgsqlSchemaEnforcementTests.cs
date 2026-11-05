using Dapper;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Tests.TestInfrastructure;
using Npgsql;

namespace FrubberPOS.Tests.Infrastructure.Schema;

/// <summary>
/// Schema enforcement on a freshly migrated, isolated database (JHO-56):
/// the DDL is the last line of defense (§6.3), so these tests attack the
/// base directly with raw SQL — foreign keys, CHECKs, unique indexes and
/// immutability triggers must reject bad writes even when no use case
/// stands in front. Each test cleans the database afterwards.
/// </summary>
/// <remarks>
/// JHO-113: limpieza vía <see cref="IAsyncLifetime"/>. La clase implementaba
/// <c>System.IAsyncDisposable</c>, que xUnit v2 no invoca en las clases de
/// prueba: cada test heredaba las filas del anterior y moría con
/// «llave duplicada».
/// </remarks>
public sealed class NpgsqlSchemaEnforcementTests : IAsyncLifetime
{
    public Task InitializeAsync() => PostgresTestDb.CleanAllAsync();

    public Task DisposeAsync() => PostgresTestDb.CleanAllAsync();

    private static async Task<NpgsqlConnection> FreshDbAsync()
    {
        await PostgresTestDb.MigrateAsync();
        var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        return connection;
    }

    private static async Task SeedUserAsync(NpgsqlConnection db, int id, string role, int active = 1)
    {
        await db.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@Id, @Name, @Role, @Active, @Salt, @Hash, 1, 1);",
            new { Id = id, Name = $"Usuario {id}", Role = role, Active = active, Salt = new byte[16], Hash = new byte[32] });
    }

    private static async Task SeedOpenCashAsync(NpgsqlConnection db, int id = 1, int userId = 1)
    {
        await db.ExecuteAsync(
            "INSERT INTO CashSession (CashSessionId, OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (@Id, 1, 0, @UserId, 'Usuario 1', 0);",
            new { Id = id, UserId = userId });
    }

    private static async Task SeedProductAsync(NpgsqlConnection db, int id = 1, string plu = "P1", int stockMilli = 0)
    {
        await db.ExecuteAsync(
            "INSERT INTO Product (ProductId, Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)"
            + " VALUES (@Id, @Plu, 'Producto', 'KG', 1000, @Stock, 1, @Locked);",
            new { Id = id, Plu = plu, Stock = stockMilli, Locked = stockMilli == 0 ? 0 : 1 });
    }

    private static async Task SeedSaleAsync(NpgsqlConnection db, int id = 1, int sessionId = 1, int userId = 1)
    {
        await db.ExecuteAsync(
            "INSERT INTO Sale (SaleId, SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
            + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (@Id, @Id, @SessionId, @UserId, 'Usuario 1', 2, 0, 1000, 0, 1000, 'CASH', 1000, 0);",
            new { Id = id, SessionId = sessionId, UserId = userId });
    }

    [Fact]
    public async Task ForeignKeys_RejectOrphanRows()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedProductAsync(db, 1);

        // SaleLine sin venta y venta sin caja deben fallar (FKs reales activadas).
        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
            "INSERT INTO SaleLine (SaleId, LineNumber, ProductId, PluSnapshot, ProductNameSnapshot, UnitSnapshot, UnitPriceCopSnapshot, QuantityMilli, LineTotalCop)"
            + " VALUES (999, 1, 1, 'P1', 'Producto', 'KG', 1000, 1000, 1000);"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
            "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot, SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (1, 777, 1, 'Usuario 1', 2, 0, 1000, 0, 1000, 'CASH', 1000, 0);"));
    }

    [Fact]
    public async Task Plu_UniqueIndex_IsCaseInsensitive()
    {
        await using var db = await FreshDbAsync();
        await SeedProductAsync(db, 1, "M1");

        // UX_Product_Plu_NoCase respalda la unicidad (RN-PRO-003).
        // JHO-113: se comprueba el SQLSTATE y no el texto: el servidor puede
        // responder en español ("llave duplicada viola restricción de unicidad")
        // y la traducción depende del locale de la máquina.
        var ex = await Assert.ThrowsAnyAsync<PostgresException>(() => db.ExecuteAsync(
            "INSERT INTO Product (Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)"
            + " VALUES ('M1', 'Otro', 'KG', 1000, 0, 1, 0);"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);

        // Y un PLU en minúsculas ni siquiera pasa el CHECK de normalización (RN-PRO-002).
        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
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
        var ex = await Assert.ThrowsAnyAsync<PostgresException>(() => SeedOpenCashAsync(db, 2));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
    }

    [Fact]
    public async Task Sale_IsImmutable_NoUpdateNoDelete()
    {
        await using var db = await FreshDbAsync();
        await SeedUserAsync(db, 1, "ADMIN");
        await SeedOpenCashAsync(db);
        await SeedSaleAsync(db);

        var update = await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
            "UPDATE Sale SET TotalCop = 1 WHERE SaleId = 1;"));
        Assert.Contains("Sale es inmutable", update.Message); // PR-001 a nivel motor
        var delete = await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
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

        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
            "UPDATE SaleLine SET QuantityMilli = 2000 WHERE SaleId = 1;"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
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
        var cashier = await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
            "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
            + " VALUES (1, 3, 0, 2, 'Usuario 2');"));
        Assert.Contains("administrador activo", cashier.Message);

        // Caja cerrada: la misma venta ya no puede anularse.
        await db.ExecuteAsync(
            "UPDATE CashSession SET ClosedAtUtcMs = 4, ClosedAtOffsetMinutes = 0, ClosedByUserId = 1, ClosedByNameSnapshot = 'Usuario 1',"
            + " ClosedCashSalesCop = 0, ClosedTransferSalesCop = 0, ClosedDiscountCop = 0, ClosedNetSalesCop = 0,"
            + " ClosedExpectedCashCop = 0, ClosedSaleCount = 1 WHERE CashSessionId = 1;");
        var closed = await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
            "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
            + " VALUES (1, 5, 0, 1, 'Usuario 1');"));
        Assert.Contains("caja abierta", closed.Message);

        // Reabrimos con una caja nueva y una venta nueva: una caja cerrada es
        // inmutable (no se puede reabrir) y la venta 1 sigue atada a esa caja
        // cerrada, así que hay que crear la pareja venta+caja abierta para tener
        // algo anulable con lo que probar la inmutabilidad del void.
        // (JHO-113: el porteo creaba la caja 2 pero anulaba la venta 2, que nunca
        // se insertó; el disparador rechazaba la anulación por "caja abierta".)
        await db.ExecuteAsync(
            "INSERT INTO CashSession (CashSessionId, OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (2, 1, 0, 1, 'Admin', 0);");
        await db.ExecuteAsync(
            "INSERT INTO Sale (SaleId, SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
            + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (2, 2, 2, 1, 'Admin', 8, 0, 1000, 0, 1000, 'CASH', 1000, 0);");
        await db.ExecuteAsync(
            "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
            + " VALUES (2, 6, 0, 1, 'Admin');");
        var updateVoid = await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
            "UPDATE SaleVoid SET VoidedAtUtcMs = 7 WHERE SaleId = 2;"));
        Assert.Contains("SaleVoid es inmutable", updateVoid.Message);
        var deleteVoid = await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
            "DELETE FROM SaleVoid WHERE SaleId = 2;"));
        Assert.Contains("SaleVoid no puede eliminarse", deleteVoid.Message);
    }
}
