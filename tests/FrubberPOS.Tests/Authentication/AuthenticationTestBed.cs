using Dapper;
using FrubberPOS.Application.Persistence;
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

    private AuthenticationTestBed(string path, Pbkdf2PinHasher hasher, DapperUserRepository users, FakeCurrentSession session)
    {
        this.path = path;
        Hasher = hasher;
        Users = users;
        Session = session;
    }

    public Pbkdf2PinHasher Hasher { get; }

    public DapperUserRepository Users { get; }

    public FakeCurrentSession Session { get; }

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
        var bed = new AuthenticationTestBed(path, hasher, new DapperUserRepository(units), new FakeCurrentSession());

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

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
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
