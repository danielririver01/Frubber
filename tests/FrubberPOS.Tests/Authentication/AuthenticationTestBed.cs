using Dapper;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Users;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Users;
using FrubberPOS.Infrastructure.Security;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Authentication;

/// <summary>
/// Real database (migrated with the approved 001), real repository and
/// real hasher, fake session and fake clock. Shared by the authentication
/// use-case tests.
/// </summary>
internal sealed class AuthenticationTestBed : IAsyncDisposable
{
    private const string InsertUser = "INSERT INTO \"User\""
        + " (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
        + " VALUES (@UserId, @DisplayName, @Role, @IsActive, @PinSalt, @PinHash, @PinIterations, @PinAlgorithmVersion);";

    private readonly string path;

    private AuthenticationTestBed(
        string path,
        Pbkdf2PinHasher hasher,
        DapperUserRepository users,
        FakeCurrentSession session,
        SqliteUnitOfWorkFactory units)
    {
        this.path = path;
        Hasher = hasher;
        Users = users;
        Session = session;
        Units = units;
    }

    public Pbkdf2PinHasher Hasher { get; }

    public DapperUserRepository Users { get; }

    public FakeCurrentSession Session { get; }

    public SqliteUnitOfWorkFactory Units { get; }

    /// <summary>
    /// Fresh admin-signed session for admin-only use cases. Never touches
    /// <see cref="Session"/> (login tests need it unsigned).
    /// </summary>
    public static FakeCurrentSession AdminSession()
    {
        var session = new FakeCurrentSession();
        session.SignIn(new SignedInUser(1, "Admin", UserRole.Admin));
        return session;
    }

    public static async Task<AuthenticationTestBed> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");
        var connectionString = $"Data Source={path}";

        var migrations = await new MigrationRunner().MigrateAsync(connectionString, CancellationToken.None);
        if (!migrations.IsSuccess)
        {
            throw new InvalidOperationException("No se pudo migrar la base de prueba.");
        }

        var hasher = new Pbkdf2PinHasher();
        var units = new SqliteUnitOfWorkFactory(new SqliteConnectionFactory(new DatabaseOptions(path)));
        var bed = new AuthenticationTestBed(path, hasher, new DapperUserRepository(), new FakeCurrentSession(), units);

        await bed.SeedUserAsync(1, "Admin", "ADMIN", "1111", isActive: true);
        await bed.SeedUserAsync(2, "Zed Cajero", "CASHIER", "2222", isActive: true);
        await bed.SeedUserAsync(3, "Ana Cajera", "CASHIER", "3333", isActive: true);
        await bed.SeedUserAsync(4, "Inactivo", "CASHIER", "4444", isActive: false);

        return bed;
    }

    public async Task SeedUserAsync(int id, string name, string role, string pin, bool isActive)
    {
        var derived = Hasher.HashPin(pin);
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(InsertUser, new
        {
            UserId = id,
            DisplayName = name,
            Role = role,
            IsActive = isActive ? 1 : 0,
            PinSalt = derived.Salt,
            PinHash = derived.Hash,
            PinIterations = derived.Iterations,
            PinAlgorithmVersion = derived.AlgorithmVersion
        });
    }

    public async Task SeedCashSessionAsync(int userId, string openedByName)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO CashSession (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1, 0, @UserId, @Name, 0);",
            new { UserId = userId, Name = openedByName });
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
