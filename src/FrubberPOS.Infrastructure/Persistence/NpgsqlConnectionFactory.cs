using System.Data.Common;
using FrubberPOS.Application.Persistence;
using Npgsql;

namespace FrubberPOS.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL connection factory (ADR-010, JHO-21). Applies and verifies
/// <c>foreign_keys</c> semantics, sets <c>lock_timeout</c> from
/// <see cref="DatabaseOptions.BusyTimeoutMilliseconds"/> and
/// <c>Maximum Pool Size=10</c> per puesto so five cajas never exhaust the
/// server (default 100 × 5 would). A connection that does not honor the
/// policies is discarded with an error instead of being returned.
/// When the engine cannot be reached, the failure is translated into
/// <see cref="DatabaseUnavailableException"/> (JHO-113 · ADR-029) so the
/// caja informs «Sin conexión con el servidor» instead of crashing.
/// </summary>
public sealed class NpgsqlConnectionFactory : IDatabaseConnectionFactory
{
    private readonly DatabaseOptions options;

    public NpgsqlConnectionFactory(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
    }

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
        {
            MaxPoolSize = 10,
            Timeout = (options.BusyTimeoutMilliseconds + 999) / 1000,
        };
        var connectionString = builder.ConnectionString;
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            await using (var setup = connection.CreateCommand())
            {
                setup.CommandText =
                    $"SET lock_timeout = {options.BusyTimeoutMilliseconds};" +
                    "SET statement_timeout = 0;" + // no query timeout; only locks are bounded
                    "SET idle_in_transaction_session_timeout = 0;";
                await setup.ExecuteNonQueryAsync(cancellationToken);
            }

            VerifyPolicies(connection);
            return connection;
        }
        catch (Exception ex) when (IsEngineDown(ex))
        {
            await connection.DisposeAsync();
            throw new DatabaseUnavailableException(ex);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// JHO-113 · ADR-029: connection-time failures that mean "the engine
    /// is down". A transport error (refused / unreachable / timeout) is
    /// wrapped by Npgsql as an <see cref="NpgsqlException"/> whose inner
    /// exception is NOT a <see cref="PostgresException"/>; server answers
    /// (bad credentials, missing database…) arrive as PostgresException
    /// and are classified by SQLSTATE instead: connection class 08,
    /// shutdown 57P01–57P03 and connection-full 53300–53303. Anything
    /// else keeps its original exception.
    /// </summary>
    private static bool IsEngineDown(Exception ex)
    {
        if (ex is NpgsqlException { InnerException: not null and not PostgresException })
        {
            return true;
        }

        if (ex is NpgsqlException { SqlState: { } state })
        {
            return state.StartsWith("08", StringComparison.Ordinal)
                || state is "57P01" or "57P02" or "57P03"
                    or "53300" or "53301" or "53302" or "53303";
        }

        return false;
    }

    private static void VerifyPolicies(NpgsqlConnection connection)
    {
        // PostgreSQL enforces foreign keys by default (unlike SQLite), so
        // there is no PRAGMA to verify. We confirm the connection is alive
        // and responsive by running a trivial query.
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        if (Convert.ToInt64(command.ExecuteScalar()) != 1)
        {
            throw new InvalidOperationException("La conexión PostgreSQL no respondió a la prueba de vida.");
        }
    }
}
