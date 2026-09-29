using System.Data.Common;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Infrastructure.Migrations;

/// <summary>
/// Applies pending migrations in version order (ADR-008, JHO-20). Each
/// script runs once, inside its own transaction together with its
/// <c>SchemaMigration</c> record: a failure rolls everything back and a
/// re-run over an updated database changes nothing.
/// </summary>
/// <remarks>
/// Connection setup here is minimal (<c>foreign_keys = ON</c>, required by
/// the scripts); JHO-21 centralizes connection creation and PRAGMAs and
/// this runner adopts it via <see cref="IDatabaseConnectionFactory"/>.
/// The pre-migration consistent backup (ADR-008)
/// is wired in JHO-53; until then the caller owns it. Timestamps come from
/// an injectable clock source until JHO-22 provides <c>IClock</c>. Scripts
/// must leave <c>SchemaMigration</c> existing: 001 creates it and it is
/// never dropped, so the runner never creates it itself.
/// </remarks>
public sealed class MigrationRunner
{
    private readonly IReadOnlyList<KnownMigration> catalog;
    private readonly Func<long> utcNowMilliseconds;

    public MigrationRunner()
        : this(MigrationCatalog.Default, SystemUtcNowMilliseconds)
    {
    }

    public MigrationRunner(IReadOnlyList<KnownMigration> catalog, Func<long> utcNowMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(utcNowMilliseconds);

        if (catalog.Select(m => m.Version).Distinct().Count() != catalog.Count)
        {
            throw new ArgumentException("El catálogo tiene versiones duplicadas.", nameof(catalog));
        }

        this.catalog = catalog;
        this.utcNowMilliseconds = utcNowMilliseconds;
    }

    public static long SystemUtcNowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <returns>Number of migrations applied by this run (0 when updated).</returns>
    public async Task<Result<int>> MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Result<int>.Failure(new Error(ErrorCode.Validation, "La cadena de conexión no puede estar vacía.", "connectionString"));
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
        }

        var applied = await ReadAppliedAsync(connection, cancellationToken);
        var ordered = catalog.OrderBy(m => m.Version).ToList();
        var latestKnown = ordered.Count == 0 ? 0 : ordered[^1].Version;

        foreach (var entry in applied)
        {
            if (entry.Key > latestKnown)
            {
                return Result<int>.Failure(new Error(
                    ErrorCode.Conflict,
                    $"La base de datos tiene la versión {entry.Key}, desconocida por esta aplicación. Actualice Frubber POS."));
            }
        }

        var appliedCount = 0;
        foreach (var migration in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (applied.TryGetValue(migration.Version, out var recorded))
            {
                if (recorded.ScriptName != migration.ScriptName
                    || recorded.Checksum != MigrationChecksum.Sha256Hex(migration.Sql))
                {
                    return Result<int>.Failure(new Error(
                        ErrorCode.Conflict,
                        $"La migración {migration.ScriptName} (versión {migration.Version}) cambió después de aplicarse. No se puede continuar."));
                }

                continue;
            }

            var outcome = await ApplyOnceAsync(connection, migration, cancellationToken);
            if (outcome.IsFailure)
            {
                return outcome;
            }

            applied[migration.Version] = (migration.ScriptName, MigrationChecksum.Sha256Hex(migration.Sql));
            appliedCount++;
        }

        return Result<int>.Success(appliedCount);
    }

    private static async Task<Dictionary<int, (string ScriptName, string Checksum)>> ReadAppliedAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var applied = new Dictionary<int, (string ScriptName, string Checksum)>();

        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaMigration';";
            if ((long)(await exists.ExecuteScalarAsync(cancellationToken))! == 0)
            {
                return applied;
            }
        }

        await using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT Version, ScriptName, Checksum FROM SchemaMigration;";
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                applied[reader.GetInt32(0)] = (reader.GetString(1), reader.GetString(2));
            }
        }

        return applied;
    }

    private async Task<Result<int>> ApplyOnceAsync(
        SqliteConnection connection,
        KnownMigration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using (var script = connection.CreateCommand())
            {
                script.Transaction = transaction;
                script.CommandText = migration.Sql;
                await script.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO SchemaMigration (Version, ScriptName, Checksum, AppliedAtUtcMs) VALUES ($version, $name, $checksum, $appliedAt);";
                record.Parameters.AddWithValue("$version", migration.Version);
                record.Parameters.AddWithValue("$name", migration.ScriptName);
                record.Parameters.AddWithValue("$checksum", MigrationChecksum.Sha256Hex(migration.Sql));
                record.Parameters.AddWithValue("$appliedAt", utcNowMilliseconds());
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
            return Result<int>.Success(1);
        }
        catch (Exception ex) when (ex is SqliteException || ex is InvalidOperationException)
        {
            // Uncommitted transaction rolls back on dispose: the migration is
            // fully reverted and nothing is recorded. User-safe message only.
            return Result<int>.Failure(new Error(
                ErrorCode.Storage,
                $"No se pudo aplicar la migración {migration.ScriptName} (versión {migration.Version}). La base quedó sin cambios."));
        }
    }
}
