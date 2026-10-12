using FrubberPOS.Application.Common;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Infrastructure.Migrations;

public sealed class MigrationRunnerTests
{
    private static readonly DateTimeOffset FixedMoment = DateTimeOffset.FromUnixTimeMilliseconds(1789248000000L);

    private const string TrackingTableSql =
        "CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, ScriptName TEXT NOT NULL, Checksum TEXT NOT NULL, AppliedAtUtcMs INTEGER NOT NULL);";

    [Fact]
    public async Task FreshDb_AppliesEmbeddedMigrationAndRecordsVersion()
    {
        var path = NewTempDbPath();
        try
        {
            var runner = new MigrationRunner(MigrationCatalog.Default, new FakeClock(FixedMoment));
            var result = await runner.MigrateAsync(ConnectionString(path), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(4, result.Value);

            await using var connection = new SqliteConnection(ConnectionString(path));
            await connection.OpenAsync();

            var (version, name, checksum, appliedAt) = await ReadRecordAsync(connection, 1);
            Assert.Equal(1, version);
            Assert.Equal("001_Initial.sql", name);
            Assert.Matches("^[0-9A-F]{64}$", checksum);
            Assert.Equal(FixedMoment.ToUnixTimeMilliseconds(), appliedAt);

            var trial = await ReadRecordAsync(connection, 2);
            Assert.Equal(2, trial.Version);
            Assert.Equal("002_Trial.sql", trial.Name);

            var openingBalance = await ReadRecordAsync(connection, 3);
            Assert.Equal(3, openingBalance.Version);
            Assert.Equal("003_OpeningBalanceCorrection.sql", openingBalance.Name);

            var scale = await ReadRecordAsync(connection, 4);
            Assert.Equal(4, scale.Version);
            Assert.Equal("004_Scale.sql", scale.Name);

            Assert.True(await TableExistsAsync(connection, "Product"));
            Assert.True(await TableExistsAsync(connection, "Sale"));
            Assert.True(await TableExistsAsync(connection, "SaleVoid"));
            Assert.True(await TableExistsAsync(connection, "TrialState"));
            Assert.True(await TableExistsAsync(connection, "ScaleSetting"));

            await using var sequence = connection.CreateCommand();
            sequence.CommandText = "SELECT LastValue FROM SystemSequence WHERE SequenceName = 'SaleNumber';";
            Assert.Equal(0L, (long)(await sequence.ExecuteScalarAsync())!);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task RepeatMigrate_ReturnsZeroAndChangesNothing()
    {
        var path = NewTempDbPath();
        try
        {
            var runner = new MigrationRunner();
            Assert.Equal(4, (await runner.MigrateAsync(ConnectionString(path), CancellationToken.None)).Value);

            var before = await RowCountAsync(path, "SchemaMigration");
            var second = await runner.MigrateAsync(ConnectionString(path), CancellationToken.None);

            Assert.True(second.IsSuccess);
            Assert.Equal(0, second.Value);
            Assert.Equal(before, await RowCountAsync(path, "SchemaMigration"));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task UpgradeFromV1_AppliesTrialOnly()
    {
        var path = NewTempDbPath();
        try
        {
            var v1 = MigrationCatalog.Default.Where(m => m.Version == 1).ToList();
            var first = await new MigrationRunner(v1, new FakeClock(FixedMoment))
                .MigrateAsync(ConnectionString(path), CancellationToken.None);
            Assert.True(first.IsSuccess);
            Assert.Equal(1, first.Value);

            var second = await new MigrationRunner(MigrationCatalog.Default, new FakeClock(FixedMoment))
                .MigrateAsync(ConnectionString(path), CancellationToken.None);

            Assert.True(second.IsSuccess);
            Assert.Equal(3, second.Value); // 002_Trial + 003_OpeningBalanceCorrection + 004_Scale

            await using var connection = new SqliteConnection(ConnectionString(path));
            await connection.OpenAsync();
            Assert.True(await TableExistsAsync(connection, "TrialState"));
            Assert.True(await TableExistsAsync(connection, "ScaleSetting"));
            Assert.Equal(4, await RowCountAsync(path, "SchemaMigration"));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task FailedMigration_RollsBackCompletely()
    {
        var path = NewTempDbPath();
        try
        {
            var catalog = new List<KnownMigration>
            {
                new(1, "001_Ok.sql", TrackingTableSql + "CREATE TABLE T1 (Id INTEGER PRIMARY KEY);"),
                new(2, "002_Bad.sql", "THIS IS NOT SQL;")
            };
            var runner = new MigrationRunner(catalog, new FakeClock(FixedMoment));

            var result = await runner.MigrateAsync(ConnectionString(path), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCode.Storage, result.Error!.Code);

            await using var connection = new SqliteConnection(ConnectionString(path));
            await connection.OpenAsync();
            Assert.False(await TableExistsAsync(connection, "T2"));
            Assert.Equal(1, await RowCountAsync(path, "SchemaMigration"));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task ChangedScriptAfterApply_IsRefused()
    {
        var path = NewTempDbPath();
        try
        {
            var first = new MigrationRunner(
                new List<KnownMigration> { new(1, "001_Ok.sql", TrackingTableSql + "CREATE TABLE T1 (Id INTEGER PRIMARY KEY);") },
                new FakeClock(FixedMoment));
            Assert.True((await first.MigrateAsync(ConnectionString(path), CancellationToken.None)).IsSuccess);

            var tampered = new MigrationRunner(
                new List<KnownMigration> { new(1, "001_Ok.sql", TrackingTableSql + "CREATE TABLE T1 (Id INTEGER PRIMARY KEY, Extra TEXT);") },
                new FakeClock(FixedMoment));
            var result = await tampered.MigrateAsync(ConnectionString(path), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
            Assert.Equal(1, await RowCountAsync(path, "SchemaMigration"));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task FutureVersionInDatabase_IsRefused()
    {
        var path = NewTempDbPath();
        try
        {
            await using (var setup = new SqliteConnection(ConnectionString(path)))
            {
                await setup.OpenAsync();
                await using var create = setup.CreateCommand();
                create.CommandText = "CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, ScriptName TEXT, Checksum TEXT, AppliedAtUtcMs INTEGER);"
                    + "INSERT INTO SchemaMigration (Version, ScriptName, Checksum, AppliedAtUtcMs) VALUES (99, '099_Future.sql', 'DEAD', 1);";
                await create.ExecuteNonQueryAsync();
            }

            var result = await new MigrationRunner().MigrateAsync(ConnectionString(path), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new MigrationRunner().MigrateAsync(ConnectionString(NewTempDbPath()), canceled.Token));
    }

    private static string ConnectionString(string path) => $"Data Source={path}";

    private static string NewTempDbPath() =>
        Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteTempDb(string path)
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
        File.Delete(path);
    }

    private static async Task<(int Version, string Name, string Checksum, long AppliedAt)> ReadRecordAsync(SqliteConnection connection, int version)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version, ScriptName, Checksum, AppliedAtUtcMs FROM SchemaMigration WHERE Version = $v;";
        command.Parameters.AddWithValue("$v", version);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3));
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table', 'view') AND name = $n;";
        command.Parameters.AddWithValue("$n", table);
        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task<long> RowCountAsync(string path, string table)
    {
        await using var connection = new SqliteConnection(ConnectionString(path));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
