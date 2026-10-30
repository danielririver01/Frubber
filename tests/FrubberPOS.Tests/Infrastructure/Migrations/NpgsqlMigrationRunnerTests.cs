using FrubberPOS.Application.Common;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Tests.TestInfrastructure;

namespace FrubberPOS.Tests.Infrastructure.Migrations;

public sealed class NpgsqlMigrationRunnerTests
{
    private static readonly DateTimeOffset FixedMoment = DateTimeOffset.FromUnixTimeMilliseconds(1789248000000L);

    [Fact]
    public async Task FreshDb_AppliesEmbeddedMigrationAndRecordsVersion()
    {
        await PostgresTestDb.CleanAllAsync();
        var runner = new MigrationRunner(MigrationCatalog.Default, new FakeClock(FixedMoment));
        var result = await runner.MigrateAsync(PostgresTestDb.ConnectionString, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value);

        await using var connection = PostgresTestDb.NewConnection();
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

    [Fact]
    public async Task RepeatMigrate_ReturnsZeroAndChangesNothing()
    {
        await PostgresTestDb.CleanAllAsync();
        var runner = new MigrationRunner();
        Assert.Equal(4, (await runner.MigrateAsync(PostgresTestDb.ConnectionString, CancellationToken.None)).Value);

        var before = await RowCountAsync("SchemaMigration");
        var second = await runner.MigrateAsync(PostgresTestDb.ConnectionString, CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(0, second.Value);
        Assert.Equal(before, await RowCountAsync("SchemaMigration"));
    }

    [Fact]
    public async Task FailedMigration_RollsBackCompletely()
    {
        await PostgresTestDb.CleanAllAsync();
        var catalog = new List<KnownMigration>
        {
            new(1, "001_Ok.sql", "CREATE TABLE T1 (Id INTEGER PRIMARY KEY);"),
            new(2, "002_Bad.sql", "THIS IS NOT SQL;")
        };
        var runner = new MigrationRunner(catalog, new FakeClock(FixedMoment));

        var result = await runner.MigrateAsync(PostgresTestDb.ConnectionString, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Storage, result.Error!.Code);

        Assert.False(await TableExistsAsync(PostgresTestDb.NewConnection(), "T2"));
        Assert.Equal(1, await RowCountAsync("SchemaMigration"));
    }

    [Fact]
    public async Task ChangedScriptAfterApply_IsRefused()
    {
        await PostgresTestDb.CleanAllAsync();
        var first = new MigrationRunner(
            new List<KnownMigration> { new(1, "001_Ok.sql", "CREATE TABLE T1 (Id INTEGER PRIMARY KEY);") },
            new FakeClock(FixedMoment));
        Assert.True((await first.MigrateAsync(PostgresTestDb.ConnectionString, CancellationToken.None)).IsSuccess);

        var tampered = new MigrationRunner(
            new List<KnownMigration> { new(1, "001_Ok.sql", "CREATE TABLE T1 (Id INTEGER PRIMARY KEY, Extra TEXT);") },
            new FakeClock(FixedMoment));
        var result = await tampered.MigrateAsync(PostgresTestDb.ConnectionString, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(1, await RowCountAsync("SchemaMigration"));
    }

    [Fact]
    public async Task FutureVersionInDatabase_IsRefused()
    {
        await PostgresTestDb.CleanAllAsync();
        await using (var setup = PostgresTestDb.NewConnection())
        {
            await setup.OpenAsync();
            await using var create = setup.CreateCommand();
            create.CommandText = "CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, ScriptName TEXT, Checksum TEXT, AppliedAtUtcMs INTEGER);"
                + "INSERT INTO SchemaMigration (Version, ScriptName, Checksum, AppliedAtUtcMs) VALUES (99, '099_Future.sql', 'DEAD', 1);";
            await create.ExecuteNonQueryAsync();
        }

        var result = await new MigrationRunner().MigrateAsync(PostgresTestDb.ConnectionString, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new MigrationRunner().MigrateAsync(PostgresTestDb.ConnectionString, canceled.Token));
    }

    private static async Task<(int Version, string Name, string Checksum, long AppliedAt)> ReadRecordAsync(NpgsqlConnection connection, int version)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version, ScriptName, Checksum, AppliedAtUtcMs FROM SchemaMigration WHERE Version = @v;";
        command.Parameters.AddWithValue("@v", version);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3));
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pg_tables WHERE tablename = @n;";
        command.Parameters.AddWithValue("@n", table);
        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task<long> RowCountAsync(string table)
    {
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
