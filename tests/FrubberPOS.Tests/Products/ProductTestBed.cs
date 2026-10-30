using Dapper;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Users;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Business;
using FrubberPOS.Infrastructure.Persistence.Cash;
using FrubberPOS.Infrastructure.Persistence.Products;
using FrubberPOS.Infrastructure.Persistence.Sales;
using FrubberPOS.Infrastructure.Persistence.Trial;
using FrubberPOS.Infrastructure.Persistence.Users;
using FrubberPOS.Infrastructure.Security;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;
using Npgsql;

namespace FrubberPOS.Tests.Products;

/// <summary>
/// Real database (migrated with the approved 001) and real repository,
/// fake sessions. Shared by the product use-case tests (JHO-32).
/// </summary>
internal sealed class ProductTestBed : IAsyncDisposable
{
    private ProductTestBed(
        DapperProductRepository products,
        DapperCashSessionRepository sessions,
        DapperSaleRepository sales,
        DapperSaleHistoryRepository history,
        DapperSaleVoidRepository voids,
        DapperBusinessSettingRepository settings,
        FakeClock clock,
        NpgsqlUnitOfWorkFactory units)
    {
        Products = products;
        Sessions = sessions;
        Sales = sales;
        History = history;
        Voids = voids;
        Settings = settings;
        Clock = clock;
        Units = units;
    }

    public DapperProductRepository Products { get; }

    public DapperCashSessionRepository Sessions { get; }

    public DapperSaleRepository Sales { get; }

    public DapperSaleHistoryRepository History { get; }

    public DapperSaleVoidRepository Voids { get; }

    public DapperBusinessSettingRepository Settings { get; }

    public FrubberPOS.Infrastructure.Persistence.Scale.DapperScaleSettingRepository ScaleSettings { get; } = new();

    /// <summary>Usuarios (repositorio real) para pruebas de autenticación/primer inicio.</summary>
    public DapperUserRepository Users { get; } = new();

    /// <summary>Hasher de PIN real (PBKDF2) para pruebas de primer inicio/login.</summary>
    public Pbkdf2PinHasher Hasher { get; } = new();

    /// <summary>Estado de la prueba de 30 días (repositorio real).</summary>
    public DapperTrialRepository Trial { get; } = new();

    /// <summary>Sesión vacía actual (no autenticado) para pruebas de login.</summary>
    public FakeCurrentSession Session { get; } = new();

    public FakeClock Clock { get; }

    public NpgsqlUnitOfWorkFactory Units { get; }

    /// <summary>Licensing gate that allows every operation until a test blocks it (JHO-57).</summary>
    public FakeOperationalGate Gate { get; } = new();

    public static FakeCurrentSession AdminSession()
    {
        var session = new FakeCurrentSession();
        session.SignIn(new SignedInUser(1, "Admin", UserRole.Admin));
        return session;
    }

    public static FakeCurrentSession CashierSession()
    {
        var session = new FakeCurrentSession();
        session.SignIn(new SignedInUser(2, "Cajero", UserRole.Cashier));
        return session;
    }

    public static async Task<ProductTestBed> CreateAsync()
    {
        await PostgresTestDb.MigrateAsync();

        // JHO-113: la suite comparte una única base y la limpieza de
        // DisposeAsync no se ejecuta si la corrida anterior murió a mitad
        // (crash del host de pruebas), así que se limpia también al crear.
        // Es idempotente: la base queda con las filas iniciales sembradas.
        await PostgresTestDb.CleanAllAsync();

        var units = PostgresTestDb.NewUnitFactory();
        return new ProductTestBed(
            new DapperProductRepository(), new DapperCashSessionRepository(), new DapperSaleRepository(),
            new DapperSaleHistoryRepository(), new DapperSaleVoidRepository(), new DapperBusinessSettingRepository(),
            new FakeClock(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(-5))), units);
    }

    /// <summary>
    /// Records one sold unit of the product so deletion must be refused.
    /// Seeds the minimum chain: user, open cash session, cash sale, line.
    /// </summary>
    public async Task SeedSaleLineAsync(int productId, string plu, string name)
    {
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@UserId, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { UserId = 1, Salt = new byte[16], Hash = new byte[32] });
        await connection.ExecuteAsync(
            "INSERT INTO CashSession (CashSessionId, OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1, 1, 0, 1, 'Admin', 0);");
        await connection.ExecuteAsync(
            "INSERT INTO Sale (SaleId, SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
            + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (1, 1, 1, 1, 'Admin', 2, 0, 2000, 0, 2000, 'CASH', 2000, 0);");
        await connection.ExecuteAsync(
            "INSERT INTO SaleLine (SaleId, LineNumber, ProductId, PluSnapshot, ProductNameSnapshot, UnitSnapshot,"
            + " UnitPriceCopSnapshot, QuantityMilli, LineTotalCop)"
            + " VALUES (1, 1, @ProductId, @Plu, @Name, 'UNIT', 2000, 1000, 2000);",
            new { ProductId = productId, Plu = plu, Name = name });
    }

    public async ValueTask DisposeAsync()
    {
        await PostgresTestDb.CleanAllAsync();
        await Task.CompletedTask;
    }
}
