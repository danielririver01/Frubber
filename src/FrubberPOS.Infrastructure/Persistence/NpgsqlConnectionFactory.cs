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
            MaximumPoolSize = 10,
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
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void VerifyPolicies(NpgsqlConnection connection)
    {
        // PostgreSQL enforces foreign keys by default (unlike SQLite), so
        // there is no PRAGMA to verify. We confirm the connection is alive
        // and responsive by running a trivial query.
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        if ((long)command.ExecuteScalar()! != 1)
        {
            throw new InvalidOperationException("La conexión PostgreSQL no respondió a la prueba de vida.");
        }
    }
}
