using System.Data.Common;
using FrubberPOS.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Infrastructure.Persistence;

/// <summary>
/// File-based SQLite connection factory (ADR-010, JHO-21). Applies and
/// verifies <c>foreign_keys = ON</c>, <c>journal_mode = WAL</c> and
/// <c>synchronous = FULL</c> on every connection; a connection that does
/// not honor them is discarded with an error instead of being returned.
/// </summary>
public sealed class SqliteConnectionFactory : IDatabaseConnectionFactory
{
    private readonly DatabaseOptions options;

    public SqliteConnectionFactory(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
    }

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = options.DatabaseFilePath }.ConnectionString;
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            await using (var setup = connection.CreateCommand())
            {
                setup.CommandText = "PRAGMA journal_mode = WAL;"
                    + "PRAGMA synchronous = FULL;"
                    + "PRAGMA foreign_keys = ON;"
                    + $"PRAGMA busy_timeout = {options.BusyTimeoutMilliseconds};";
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

    private static void VerifyPolicies(SqliteConnection connection)
    {
        if (!string.Equals(QuerySingle(connection, "PRAGMA journal_mode;"), "wal", StringComparison.OrdinalIgnoreCase)
            || QuerySingle(connection, "PRAGMA synchronous;") != "2"
            || QuerySingle(connection, "PRAGMA foreign_keys;") != "1")
        {
            throw new InvalidOperationException("La conexión SQLite no aplicó las políticas requeridas (WAL, synchronous FULL, claves foráneas).");
        }
    }

    private static string? QuerySingle(SqliteConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
        command.CommandText = pragma;
        return command.ExecuteScalar()?.ToString();
    }
}
