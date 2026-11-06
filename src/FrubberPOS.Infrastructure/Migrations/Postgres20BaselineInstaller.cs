using System.Data.Common;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using Npgsql;

namespace FrubberPOS.Infrastructure.Migrations;

/// <summary>
/// Server-side bootstrap for a new, dedicated 2.0 database. This is not wired
/// into desktop startup: the 2.0 application and security provisioning are not
/// ready. It never upgrades, drops, or converts an existing customer database.
/// </summary>
public sealed class Postgres20BaselineInstaller
{
    private const string ResourceName = "FrubberPOS.Infrastructure.Migrations.V2.001_Baseline.sql";
    private const string ScriptName = "001_Baseline.sql";
    private readonly Func<DbTransaction, CancellationToken, Task> commitAsync;
    private readonly Func<DbConnection, DbTransaction, CancellationToken, Task>? afterScriptAsync;

    public Postgres20BaselineInstaller()
        : this(static (transaction, token) => transaction.CommitAsync(token), null)
    {
    }

    // Internal injection points let integration tests simulate a failure after
    // DDL or an unacknowledged COMMIT. Production always uses the constructor
    // above and executes only the embedded baseline.
    internal Postgres20BaselineInstaller(
        Func<DbTransaction, CancellationToken, Task> commitAsync,
        Func<DbConnection, DbTransaction, CancellationToken, Task>? afterScriptAsync)
    {
        this.commitAsync = commitAsync ?? throw new ArgumentNullException(nameof(commitAsync));
        this.afterScriptAsync = afterScriptAsync;
    }

    public async Task<Result<int>> InstallAsync(IDatabaseConnectionFactory factory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var stream = typeof(Postgres20BaselineInstaller).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("No se encontró la línea base 2.0 embebida.");
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync(cancellationToken);
        var checksum = MigrationChecksum.Sha256Hex(script);

        await using var connection = await factory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serializes cooperating installers. External tools/admins must not
            // modify this dedicated database while installation is running.
            await using (var setup = connection.CreateCommand())
            {
                setup.Transaction = transaction;
                // Keep public as the creation schema but let PostgreSQL search
                // pg_catalog first implicitly; explicit public-before-pg_catalog
                // would let user-defined functions shadow system functions.
                setup.CommandText = "SELECT pg_catalog.pg_advisory_xact_lock(1179796818, 20); SET LOCAL search_path = public, pg_temp;";
                await setup.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT pg_catalog.to_regclass('public.schema_migration') IS NOT NULL;";
                if ((bool)(await check.ExecuteScalarAsync(cancellationToken))!)
                {
                    // Do not query an impostor view (or a row-security policy)
                    // that could execute untrusted code during validation.
                    await using var kind = connection.CreateCommand();
                    kind.Transaction = transaction;
                    kind.CommandText = """
                        SELECT COALESCE((SELECT c.relkind = 'r' AND c.relpersistence = 'p' AND NOT c.relrowsecurity
                                           FROM pg_catalog.pg_class c
                                          WHERE c.oid = pg_catalog.to_regclass('public.schema_migration')), false);
                        """;
                    if (!(bool)(await kind.ExecuteScalarAsync(cancellationToken))!)
                    {
                        return Conflict();
                    }
                    await using var mixed = connection.CreateCommand();
                    mixed.Transaction = transaction;
                    mixed.CommandText = "SELECT pg_catalog.to_regclass('public.schemamigration') IS NOT NULL;";
                    if ((bool)(await mixed.ExecuteScalarAsync(cancellationToken))!)
                    {
                        return Conflict();
                    }
                    await using var ledger = connection.CreateCommand();
                    ledger.Transaction = transaction;
                    ledger.CommandText = "SELECT version, script_name, checksum FROM public.schema_migration ORDER BY version;";
                    await using var rows = await ledger.ExecuteReaderAsync(cancellationToken);
                    if (!await rows.ReadAsync(cancellationToken)
                        || rows.GetInt32(0) != 1 || rows.GetString(1) != ScriptName || rows.GetString(2) != checksum
                        || await rows.ReadAsync(cancellationToken))
                    {
                        return Conflict();
                    }
                    // Dispose the reader before completing the transaction.
                    await rows.DisposeAsync();
                    return await CommitWithOutcomeAsync(transaction, 0, cancellationToken);
                }
            }

            // Reject known user objects, including schema objects not listed
            // in pg_class/proc/type (e.g. collations) and selected database-wide
            // objects. This is defense in depth, not proof of an empty database:
            // the caller must supply an exclusively dedicated new database.
            await using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = """
                    SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace
                                    WHERE nspname <> 'public' AND nspname <> 'information_schema'
                                      AND nspname NOT LIKE 'pg\_%' ESCAPE '\')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'public')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_type t JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'public')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_depend d JOIN pg_catalog.pg_namespace n ON n.oid = d.refobjid
                                   WHERE d.refclassid = 'pg_catalog.pg_namespace'::pg_catalog.regclass AND n.nspname = 'public' AND d.deptype = 'n')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_extension WHERE extname <> 'plpgsql')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_publication)
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_subscription)
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_foreign_data_wrapper)
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_foreign_server)
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_event_trigger)
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_largeobject_metadata)
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_language WHERE lanname NOT IN ('internal', 'c', 'sql', 'plpgsql'));
                    """;
                if ((bool)(await check.ExecuteScalarAsync(cancellationToken))!)
                {
                    return Conflict();
                }
            }

            await using (var apply = connection.CreateCommand())
            {
                apply.Transaction = transaction;
                apply.CommandText = script;
                await apply.ExecuteNonQueryAsync(cancellationToken);
            }
            if (afterScriptAsync is not null)
            {
                await afterScriptAsync(connection, transaction, cancellationToken);
            }
            await using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO public.schema_migration (version, script_name, checksum) VALUES (1, @name, @checksum);";
                var name = record.CreateParameter();
                name.ParameterName = "name";
                name.Value = ScriptName;
                record.Parameters.Add(name);
                var hash = record.CreateParameter();
                hash.ParameterName = "checksum";
                hash.Value = checksum;
                record.Parameters.Add(hash);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }
            return await CommitWithOutcomeAsync(transaction, 1, cancellationToken);
        }
        catch (Exception ex) when (ex is NpgsqlException || ex is InvalidOperationException)
        {
            // Before commit, disposal rolls back script and ledger together.
            // A broken connection during COMMIT can leave the outcome unknown;
            // never claim that the database is unchanged in that case.
            return UnknownOutcome();
        }
    }

    private async Task<Result<int>> CommitWithOutcomeAsync(
        DbTransaction transaction, int applied, CancellationToken cancellationToken)
    {
        // Once commit is attempted, cancellation or a transport failure can
        // happen after the server committed but before the acknowledgement.
        // Conservatively treat every such failure as an unknown outcome.
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await commitAsync(transaction, cancellationToken);
            return Result<int>.Success(applied);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException
            or IOException or TimeoutException or OperationCanceledException)
        {
            return UnknownOutcome();
        }
    }

    private static Result<int> UnknownOutcome() => Result<int>.Failure(new Error(ErrorCode.Storage,
        "No se pudo confirmar el resultado de la preparación 2.0. Revise el historial en el servidor antes de continuar."));

    private static Result<int> Conflict() => Result<int>.Failure(new Error(ErrorCode.Conflict,
        "La base no está vacía o su historial 2.0 no coincide. No se aplicaron cambios."));
}
