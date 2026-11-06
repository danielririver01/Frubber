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
                setup.CommandText = "SELECT pg_advisory_xact_lock(1179796818, 20); SET LOCAL search_path = public, pg_catalog;";
                await setup.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT to_regclass('public.schema_migration') IS NOT NULL;";
                if ((bool)(await check.ExecuteScalarAsync(cancellationToken))!)
                {
                    await using var mixed = connection.CreateCommand();
                    mixed.Transaction = transaction;
                    mixed.CommandText = "SELECT to_regclass('public.schemamigration') IS NOT NULL;";
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
                    await transaction.CommitAsync(cancellationToken);
                    return Result<int>.Success(0);
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
                    SELECT EXISTS (SELECT 1 FROM pg_namespace
                                    WHERE nspname <> 'public' AND nspname <> 'information_schema'
                                      AND nspname NOT LIKE 'pg\_%' ESCAPE '\')
                        OR EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public')
                        OR EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'public')
                        OR EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'public')
                        OR EXISTS (SELECT 1 FROM pg_depend d JOIN pg_namespace n ON n.oid = d.refobjid
                                   WHERE d.refclassid = 'pg_namespace'::regclass AND n.nspname = 'public' AND d.deptype = 'n')
                        OR EXISTS (SELECT 1 FROM pg_extension WHERE extname <> 'plpgsql')
                        OR EXISTS (SELECT 1 FROM pg_publication)
                        OR EXISTS (SELECT 1 FROM pg_subscription)
                        OR EXISTS (SELECT 1 FROM pg_foreign_data_wrapper)
                        OR EXISTS (SELECT 1 FROM pg_foreign_server)
                        OR EXISTS (SELECT 1 FROM pg_event_trigger)
                        OR EXISTS (SELECT 1 FROM pg_largeobject_metadata)
                        OR EXISTS (SELECT 1 FROM pg_language WHERE lanname NOT IN ('internal', 'c', 'sql', 'plpgsql'));
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
            await transaction.CommitAsync(cancellationToken);
            return Result<int>.Success(1);
        }
        catch (Exception ex) when (ex is NpgsqlException || ex is InvalidOperationException)
        {
            // Before commit, disposal rolls back script and ledger together.
            // A broken connection during COMMIT can leave the outcome unknown;
            // never claim that the database is unchanged in that case.
            return Result<int>.Failure(new Error(ErrorCode.Storage,
                "No se pudo confirmar el resultado de la preparación 2.0. Revise el historial en el servidor antes de continuar."));
        }
    }

    private static Result<int> Conflict() => Result<int>.Failure(new Error(ErrorCode.Conflict,
        "La base no está vacía o su historial 2.0 no coincide. No se aplicaron cambios."));
}
