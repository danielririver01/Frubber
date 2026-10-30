namespace FrubberPOS.Application.Persistence;

/// <summary>
/// Database configuration for PostgreSQL. The connection string is supplied
/// by the caller (production reads <c>FRUBBERPOS_CONNECTION_STRING</c>);
/// <see cref="busyTimeoutMilliseconds"/> maps to <c>lock_timeout</c> on the
/// server side so a contended sale fails fast instead of waiting forever.
/// </summary>
public sealed record DatabaseOptions
{
    public DatabaseOptions(string connectionString, int busyTimeoutMilliseconds = 5000)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("La cadena de conexión no puede estar vacía.", nameof(connectionString));
        }

        if (busyTimeoutMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(busyTimeoutMilliseconds), "El timeout no puede ser negativo.");
        }

        ConnectionString = connectionString;
        BusyTimeoutMilliseconds = busyTimeoutMilliseconds;
    }

    public string ConnectionString { get; }

    public int BusyTimeoutMilliseconds { get; }
}
