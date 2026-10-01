using Dapper;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Users;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Business;
using FrubberPOS.Infrastructure.Persistence.Cash;
using FrubberPOS.Infrastructure.Persistence.Products;
using FrubberPOS.Infrastructure.Persistence.Sales;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Products;

/// <summary>
/// Real database (migrated with the approved 001) and real repository,
/// fake sessions. Shared by the product use-case tests (JHO-32).
/// </summary>
internal sealed class ProductTestBed : IAsyncDisposable
{
    private readonly string path;

    private ProductTestBed(
        string path,
        DapperProductRepository products,
        DapperCashSessionRepository sessions,
        DapperSaleRepository sales,
        DapperSaleHistoryRepository history,
        DapperBusinessSettingRepository settings,
        FakeClock clock,
        SqliteUnitOfWorkFactory units)
    {
        this.path = path;
        Products = products;
        Sessions = sessions;
        Sales = sales;
        History = history;
        Settings = settings;
        Clock = clock;
        Units = units;
    }

    public DapperProductRepository Products { get; }

    public string DbPath => path;

    public DapperCashSessionRepository Sessions { get; }

    public DapperSaleRepository Sales { get; }

    public DapperSaleHistoryRepository History { get; }

    public DapperBusinessSettingRepository Settings { get; }

    public FakeClock Clock { get; }

    public SqliteUnitOfWorkFactory Units { get; }

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
        var path = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");
        var connectionString = $"Data Source={path}";

        var migrations = await new MigrationRunner().MigrateAsync(connectionString, CancellationToken.None);
        if (!migrations.IsSuccess)
        {
            throw new InvalidOperationException("No se pudo migrar la base de prueba.");
        }

        var units = new SqliteUnitOfWorkFactory(new SqliteConnectionFactory(new DatabaseOptions(path)));
        return new ProductTestBed(
            path, new DapperProductRepository(), new DapperCashSessionRepository(), new DapperSaleRepository(),
            new DapperSaleHistoryRepository(), new DapperBusinessSettingRepository(),
            new FakeClock(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(-5))), units);
    }

    /// <summary>
    /// Records one sold unit of the product so deletion must be refused.
    /// Seeds the minimum chain: user, open cash session, cash sale, line.
    /// </summary>
    public async Task SeedSaleLineAsync(int productId, string plu, string name)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
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
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        foreach (var journal in new[] { path + "-wal", path + "-shm", path + "-journal" })
        {
            if (File.Exists(journal))
            {
                File.Delete(journal);
            }
        }

        await Task.CompletedTask;
    }
}
