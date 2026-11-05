using FrubberPOS.Application.Common;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;
using Npgsql;

namespace FrubberPOS.Tests.Infrastructure.Migrations;

public sealed class NpgsqlMigrationRunnerTests
{
    private static readonly DateTimeOffset FixedMoment = DateTimeOffset.FromUnixTimeMilliseconds(1789248000000L);

    // JHO-113: los catálogos sintéticos deben crear también SchemaMigration;
    // el ejecutor nunca la crea (001_Initial.sql la abre, ADR-008/JHO-20) y
    // sin ella el INSERT del registro falla y la migración «buena» salía mal.
    private const string OkScript =
        "CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, ScriptName TEXT, Checksum TEXT, AppliedAtUtcMs BIGINT);"
        + "CREATE TABLE T1 (Id INTEGER PRIMARY KEY);";

    private const string OkScriptTampered =
        "CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, ScriptName TEXT, Checksum TEXT, AppliedAtUtcMs BIGINT);"
        + "CREATE TABLE T1 (Id INTEGER PRIMARY KEY, Extra TEXT);";

    [Fact]
    public async Task FreshDb_AppliesEmbeddedMigrationAndRecordsVersion()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var runner = new MigrationRunner(MigrationCatalog.Default, new FakeClock(FixedMoment));
        var result = await runner.MigrateAsync(db.ConnectionString, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value);

        await using var connection = db.NewConnection();
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
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var runner = new MigrationRunner();
        Assert.Equal(4, (await runner.MigrateAsync(db.ConnectionString, CancellationToken.None)).Value);

        var before = await RowCountAsync(db.ConnectionString, "SchemaMigration");
        var second = await runner.MigrateAsync(db.ConnectionString, CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(0, second.Value);
        Assert.Equal(before, await RowCountAsync(db.ConnectionString, "SchemaMigration"));
    }

    [Fact]
    public async Task FailedMigration_RollsBackCompletely()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var catalog = new List<KnownMigration>
        {
            new(1, "001_Ok.sql", OkScript),
            // Crea T2 y después falla en la misma transacción: si el rollback
            // no fuera completo, T2 sobreviviría. (JHO-113: antes el guión no
            // creaba nada y la aserción pasaba sobre una conexión sin abrir.)
            new(2, "002_Bad.sql", "CREATE TABLE T2 (Id INTEGER PRIMARY KEY); THIS IS NOT SQL;")
        };
        var runner = new MigrationRunner(catalog, new FakeClock(FixedMoment));

        var result = await runner.MigrateAsync(db.ConnectionString, CancellationToken.None);

        Assert.True(result.IsFailure, result.Error?.Message);
        Assert.Equal(ErrorCode.Storage, result.Error!.Code);

        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        Assert.Contains("t1", await ListTablesAsync(connection));
        Assert.DoesNotContain("t2", await ListTablesAsync(connection));
        Assert.Equal(1, await RowCountAsync(db.ConnectionString, "SchemaMigration"));
    }

    [Fact]
    public async Task ChangedScriptAfterApply_IsRefused()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var first = new MigrationRunner(
            new List<KnownMigration> { new(1, "001_Ok.sql", OkScript) },
            new FakeClock(FixedMoment));
        Assert.True((await first.MigrateAsync(db.ConnectionString, CancellationToken.None)).IsSuccess,
            "La primera aplicación del guión 001_Ok.sql debió salir bien.");

        var tampered = new MigrationRunner(
            new List<KnownMigration> { new(1, "001_Ok.sql", OkScriptTampered) },
            new FakeClock(FixedMoment));
        var result = await tampered.MigrateAsync(db.ConnectionString, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(1, await RowCountAsync(db.ConnectionString, "SchemaMigration"));
    }

    [Fact]
    public async Task FutureVersionInDatabase_IsRefused()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        await using (var setup = db.NewConnection())
        {
            await setup.OpenAsync();
            await using var create = setup.CreateCommand();
            create.CommandText = "CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, ScriptName TEXT, Checksum TEXT, AppliedAtUtcMs BIGINT);"
                + "INSERT INTO SchemaMigration (Version, ScriptName, Checksum, AppliedAtUtcMs) VALUES (99, '099_Future.sql', 'DEAD', 1);";
            await create.ExecuteNonQueryAsync();
        }

        var result = await new MigrationRunner().MigrateAsync(db.ConnectionString, CancellationToken.None);

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

    private static async Task<List<string>> ListTablesAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename;";
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pg_tables WHERE lower(tablename) = lower(@n);";
        command.Parameters.AddWithValue("@n", table);
        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task<long> RowCountAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
