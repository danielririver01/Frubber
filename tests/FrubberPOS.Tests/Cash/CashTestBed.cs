using Dapper;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Users;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Cash;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Cash;

/// <summary>
/// Real database (migrated with the approved 001) and real cash repository,
/// fake session and clock (JHO-38 tests; the clock ticks deterministically).
/// </summary>
internal sealed class CashTestBed : IAsyncDisposable
{
    private readonly string path;

    private CashTestBed(string path, DapperCashSessionRepository sessions, FakeCurrentSession session, SqliteUnitOfWorkFactory units)
    {
        this.path = path;
        DbPath = path;
        Sessions = sessions;
        Session = session;
        Units = units;
    }

    public DapperCashSessionRepository Sessions { get; }

    public FakeCurrentSession Session { get; }

    public SqliteUnitOfWorkFactory Units { get; }

    public string DbPath { get; }

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(-5)));

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

    public static async Task<CashTestBed> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");
        var connectionString = $"Data Source={path}";

        var migrations = await new MigrationRunner().MigrateAsync(connectionString, CancellationToken.None);
        if (!migrations.IsSuccess)
        {
            throw new InvalidOperationException("No se pudo migrar la base de prueba.");
        }

        var units = new SqliteUnitOfWorkFactory(new SqliteConnectionFactory(new DatabaseOptions(path)));
        var bed = new CashTestBed(path, new DapperCashSessionRepository(), new FakeCurrentSession(), units);

        await bed.SeedUserAsync(1, "Admin", "ADMIN");
        await bed.SeedUserAsync(2, "Zed Cajero", "CASHIER");
        return bed;
    }

    public async Task SeedUserAsync(int id, string name, string role)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@UserId, @DisplayName, @Role, 1, @Salt, @Hash, 1, 1);",
            new { UserId = id, DisplayName = name, Role = role, Salt = new byte[16], Hash = new byte[32] });
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
