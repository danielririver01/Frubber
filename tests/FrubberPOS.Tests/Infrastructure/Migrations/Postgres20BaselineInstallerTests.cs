using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Tests.TestInfrastructure;
using Npgsql;

namespace FrubberPOS.Tests.Infrastructure.Migrations;

// All destructive operations are restricted to random scratch databases.
public sealed class Postgres20BaselineInstallerTests
{
    private static NpgsqlConnectionFactory Factory(string connectionString) =>
        new(new DatabaseOptions(connectionString));

    [Fact]
    public async Task FreshDatabase_InstallsBaselineOnce_AndLegacyRunnerRefusesIt()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var installer = new Postgres20BaselineInstaller();
        var first = await installer.InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.Equal(1, first.Value);
        var second = await installer.InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(second.IsSuccess, second.Error?.Message);
        Assert.Equal(0, second.Value);

        var legacy = await new MigrationRunner().MigrateAsync(db.ConnectionString, CancellationToken.None);
        Assert.True(legacy.IsFailure);
        Assert.Equal(ErrorCode.Conflict, legacy.Error!.Code);
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarLong(connection, "SELECT count(*) FROM public.schema_migration WHERE version = 1;"));
        Assert.Equal(0L, await ScalarLong(connection, "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname='schemamigration';"));
        Assert.Equal(0L, await ScalarLong(connection, "SELECT last_number FROM public.ticket_counter WHERE scope='venta';"));
    }

    [Fact]
    public async Task ExistingLegacyDatabase_IsNotConvertedOrModified()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var legacy = await new MigrationRunner().MigrateAsync(db.ConnectionString, CancellationToken.None);
        Assert.True(legacy.IsSuccess, legacy.Error?.Message);
        var result = await new Postgres20BaselineInstaller().InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        Assert.Equal(4L, await ScalarLong(connection, "SELECT count(*) FROM public.schemamigration;"));
        Assert.Equal(0L, await ScalarLong(connection, "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname='schema_migration';"));
    }

    [Theory]
    [InlineData("CREATE TABLE public.unrelated(id integer); INSERT INTO public.unrelated VALUES (7);", "SELECT count(*) FROM public.unrelated WHERE id=7;")]
    [InlineData("CREATE SCHEMA unrelated;", "SELECT count(*) FROM pg_namespace WHERE nspname='unrelated';")]
    [InlineData("CREATE FUNCTION public.unrelated() RETURNS integer LANGUAGE sql AS 'SELECT 7';", "SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proname='unrelated';")]
    [InlineData("CREATE COLLATION public.unrelated (provider = libc, locale = 'C');", "SELECT count(*) FROM pg_collation c JOIN pg_namespace n ON n.oid=c.collnamespace WHERE n.nspname='public' AND c.collname='unrelated';")]
    [InlineData("CREATE PUBLICATION unrelated;", "SELECT count(*) FROM pg_publication WHERE pubname='unrelated';")]
    [InlineData("SELECT lo_create(0);", "SELECT count(*) FROM pg_largeobject_metadata;")]
    public async Task NonemptyDatabase_IsNotChanged(string setupSql, string verifySql)
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand(setupSql, connection)) await setup.ExecuteNonQueryAsync();
        var result = await new Postgres20BaselineInstaller().InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(1L, await ScalarLong(connection, verifySql));
        Assert.Equal(0L, await ScalarLong(connection, "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname='schema_migration';"));
    }

    [Fact]
    public async Task ExistingShadowFunctions_AreNotExecutedDuringPreflight()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand("""
            CREATE FUNCTION public.to_regclass(text) RETURNS pg_catalog.regclass
                LANGUAGE plpgsql AS $$BEGIN RAISE EXCEPTION 'shadow to_regclass'; END$$;
            CREATE FUNCTION public.pg_advisory_xact_lock(integer, integer) RETURNS void
                LANGUAGE plpgsql AS $$BEGIN RAISE EXCEPTION 'shadow advisory lock'; END$$;
            """, connection))
            await setup.ExecuteNonQueryAsync();
        var unsafeSearchPath = new NpgsqlConnectionStringBuilder(db.ConnectionString)
        {
            SearchPath = "public,pg_catalog"
        }.ConnectionString;

        var result = await new Postgres20BaselineInstaller().InstallAsync(Factory(unsafeSearchPath), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(2L, await ScalarLong(connection, "SELECT count(*) FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proname IN ('to_regclass','pg_advisory_xact_lock');"));
        Assert.Equal(0L, await ScalarLong(connection, "SELECT count(*) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname='schema_migration';"));
    }

    [Fact]
    public async Task LegacyGuard_IgnoresShadowToRegclass_InUntrustedSearchPath()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand("""
            CREATE TABLE public.schema_migration(version integer);
            CREATE FUNCTION public.to_regclass(text) RETURNS pg_catalog.regclass
                LANGUAGE plpgsql AS $$BEGIN RAISE EXCEPTION 'shadow to_regclass'; END$$;
            """, connection))
            await setup.ExecuteNonQueryAsync();
        var unsafeSearchPath = new NpgsqlConnectionStringBuilder(db.ConnectionString)
        {
            SearchPath = "public,pg_catalog"
        }.ConnectionString;
        var result = await new MigrationRunner().MigrateAsync(unsafeSearchPath, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(0L, await ScalarLong(connection, "SELECT count(*) FROM public.schema_migration;"));
    }

    [Fact]
    public async Task LedgerView_IsRejectedWithoutExecutingItsQuery()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand("""
            CREATE FUNCTION public.ledger_trap() RETURNS integer LANGUAGE plpgsql
                AS $$BEGIN RAISE EXCEPTION 'ledger view executed'; END$$;
            CREATE VIEW public.schema_migration AS
                SELECT public.ledger_trap() AS version, '001_Baseline.sql'::text AS script_name, repeat('A',64) AS checksum;
            """, connection))
            await setup.ExecuteNonQueryAsync();
        var result = await new Postgres20BaselineInstaller().InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(1L, await ScalarLong(connection, "SELECT count(*) FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass('public.schema_migration') AND relkind='v';"));
    }

    [Fact]
    public async Task LedgerWithRowSecurity_IsRejected()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand("CREATE TABLE public.schema_migration(version integer, script_name text, checksum text); ALTER TABLE public.schema_migration ENABLE ROW LEVEL SECURITY;", connection))
            await setup.ExecuteNonQueryAsync();
        var result = await new Postgres20BaselineInstaller().InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(0L, await ScalarLong(connection, "SELECT count(*) FROM public.schema_migration;"));
    }

    [Fact]
    public async Task ChangedOrFutureLedger_IsRefused()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var installer = new Postgres20BaselineInstaller();
        Assert.True((await installer.InstallAsync(Factory(db.ConnectionString), CancellationToken.None)).IsSuccess);
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("INSERT INTO public.schema_migration(version,script_name,checksum) VALUES (2,'future.sql',repeat('A',64));", connection))
            await command.ExecuteNonQueryAsync();
        var result = await installer.InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(2L, await ScalarLong(connection, "SELECT count(*) FROM public.schema_migration;"));
    }

    [Fact]
    public async Task ChangedChecksum_IsRefused()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var installer = new Postgres20BaselineInstaller();
        Assert.True((await installer.InstallAsync(Factory(db.ConnectionString), CancellationToken.None)).IsSuccess);
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("UPDATE public.schema_migration SET checksum=repeat('A',64) WHERE version=1;", connection))
            await command.ExecuteNonQueryAsync();
        var result = await installer.InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
    }

    [Fact]
    public async Task MixedOrUnrecordedBaseline_IsNeverProcessedByLegacyRunner()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("CREATE TABLE public.schema_migration(version integer); CREATE TABLE public.schemamigration(version integer);", connection))
            await command.ExecuteNonQueryAsync();
        var legacy = await new MigrationRunner().MigrateAsync(db.ConnectionString, CancellationToken.None);
        Assert.True(legacy.IsFailure);
        Assert.Equal(ErrorCode.Conflict, legacy.Error!.Code);
        var baseline = await new Postgres20BaselineInstaller().InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(baseline.IsFailure);
        Assert.Equal(ErrorCode.Conflict, baseline.Error!.Code);
        Assert.Equal(0L, await ScalarLong(connection, "SELECT count(*) FROM public.schemamigration;"));
        Assert.Equal(0L, await ScalarLong(connection, "SELECT count(*) FROM public.schema_migration;"));
    }

    [Fact]
    public async Task FailureAfterBaseline_RollsBackSchemaAndLedger_ThenCanRetry()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var installer = new Postgres20BaselineInstaller(
            static (transaction, token) => transaction.CommitAsync(token),
            async (connection, transaction, token) =>
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT 1 / 0;";
                await command.ExecuteNonQueryAsync(token);
            });

        var result = await installer.InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Storage, result.Error!.Code);
        await using (var connection = db.NewConnection())
        {
            await connection.OpenAsync();
            Assert.Equal(0L, await ScalarLong(connection, "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname IN ('schema_migration','ticket_counter');"));
        }
        var retry = await new Postgres20BaselineInstaller().InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(retry.IsSuccess, retry.Error?.Message);
        Assert.Equal(1, retry.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAcknowledgementLost_ReturnsUnknownEvenWhenDatabaseCommitted(bool cancelled)
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var installer = new Postgres20BaselineInstaller(
            async (transaction, token) =>
            {
                // Simulates a lost response after the server committed; this is
                // not a physical network disconnection or an Npgsql fault test.
                await transaction.CommitAsync(token);
                if (cancelled) throw new OperationCanceledException("Simulated cancellation during acknowledgement.");
                throw new IOException("Simulated lost acknowledgement.");
            },
            null);

        var result = await installer.InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Storage, result.Error!.Code);
        Assert.Contains("Revise el historial", result.Error.Message);
        Assert.DoesNotContain("sin cambios", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        await using (var connection = db.NewConnection())
        {
            await connection.OpenAsync();
            Assert.Equal(1L, await ScalarLong(connection, "SELECT count(*) FROM public.schema_migration WHERE version=1;"));
            Assert.Equal(0L, await ScalarLong(connection, "SELECT last_number FROM public.ticket_counter WHERE scope='venta';"));
        }
        var retry = await new Postgres20BaselineInstaller().InstallAsync(Factory(db.ConnectionString), CancellationToken.None);
        Assert.True(retry.IsSuccess, retry.Error?.Message);
        Assert.Equal(0, retry.Value);
    }

    [CommitCutFact]
    public async Task CommitConnectionCut_ReturnsUnknownAndDirectRetryFindsInstalledBaseline()
    {
        var testConnection = new NpgsqlConnectionStringBuilder(PostgresTestDb.ConnectionString);
        Assert.True(testConnection.Host is "localhost" or "127.0.0.1",
            "This test requires a disposable PostgreSQL cluster on IPv4 loopback.");
        await using var db = await PostgresTestDb.CreateScratchAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var cut = DropCommitResponseAsync(listener, testConnection.Port, timeout.Token);
        var throughProxy = new NpgsqlConnectionStringBuilder(db.ConnectionString)
        {
            Host = "127.0.0.1",
            Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            SslMode = SslMode.Disable,
            Pooling = false,
        };

        var installer = new Postgres20BaselineInstaller();
        var install = installer.InstallAsync(Factory(throughProxy.ConnectionString), timeout.Token);
        await Task.WhenAll(cut, install);
        var result = await install;
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Storage, result.Error!.Code);
        Assert.Contains("Revise el historial", result.Error.Message);

        await using var connection = db.NewConnection();
        await connection.OpenAsync(timeout.Token);
        Assert.Equal(1L, await ScalarLong(connection, "SELECT count(*) FROM public.schema_migration WHERE version=1;"));
        Assert.Equal(0L, await ScalarLong(connection, "SELECT last_number FROM public.ticket_counter WHERE scope='venta';"));
        var retry = await installer.InstallAsync(Factory(db.ConnectionString), timeout.Token);
        Assert.True(retry.IsSuccess, retry.Error?.Message);
        Assert.Equal(0, retry.Value);
    }

    private static async Task DropCommitResponseAsync(TcpListener listener, int serverPort, CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        using var server = new TcpClient();
        await server.ConnectAsync(IPAddress.Loopback, serverPort, token);
        var clientStream = client.GetStream();
        var serverStream = server.GetStream();
        using var relay = CancellationTokenSource.CreateLinkedTokenSource(token);
        var requests = clientStream.CopyToAsync(serverStream, relay.Token);
        try
        {
            // Plain PostgreSQL backend frames: one type byte, four length
            // bytes (including the length itself), then the message payload.
            var header = new byte[5];
            while (true)
            {
                await serverStream.ReadExactlyAsync(header, token);
                var payload = new byte[BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1)) - 4];
                await serverStream.ReadExactlyAsync(payload, token);
                if (header[0] == 'C' && payload.AsSpan().SequenceEqual("COMMIT\0"u8))
                {
                    return;
                }
                await clientStream.WriteAsync(header, token);
                await clientStream.WriteAsync(payload, token);
            }
        }
        finally
        {
            await relay.CancelAsync();
            try
            {
                await requests;
            }
            catch (OperationCanceledException) when (relay.IsCancellationRequested)
            {
                // Stop forwarding requests before closing both sockets.
            }
        }
    }

    [Fact]
    public async Task ConcurrentInstallers_SerializeAndApplyOnce()
    {
        await using var db = await PostgresTestDb.CreateScratchAsync();
        var installer = new Postgres20BaselineInstaller();
        var results = await Task.WhenAll(
            installer.InstallAsync(Factory(db.ConnectionString), CancellationToken.None),
            installer.InstallAsync(Factory(db.ConnectionString), CancellationToken.None));
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error?.Message));
        Assert.Equal(new[] { 0, 1 }, results.Select(result => result.Value).Order().ToArray());
    }

    private static async Task<long> ScalarLong(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class CommitCutFactAttribute : FactAttribute
    {
        public CommitCutFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("FRUBBER_JHO136_ISOLATED") != "1")
            {
                Skip = "Enable FRUBBER_JHO136_ISOLATED=1 only for a disposable local PostgreSQL cluster.";
            }
        }
    }
}
