namespace FrubberPOS.Application.Persistence;

/// <summary>
/// File database configuration. The file path is supplied by the caller
/// (a stable Windows path); in production it is
/// <c>%ProgramData%\FrubberPOS\frubberpos.db</c> and resolving that
/// well-known path belongs to JHO-22.
/// </summary>
public sealed record DatabaseOptions
{
    public DatabaseOptions(string databaseFilePath, int busyTimeoutMilliseconds = 5000)
    {
        if (string.IsNullOrWhiteSpace(databaseFilePath))
        {
            throw new ArgumentException("La ruta del archivo de base de datos no puede estar vacía.", nameof(databaseFilePath));
        }

        if (busyTimeoutMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(busyTimeoutMilliseconds), "El timeout no puede ser negativo.");
        }

        DatabaseFilePath = databaseFilePath;
        BusyTimeoutMilliseconds = busyTimeoutMilliseconds;
    }

    public string DatabaseFilePath { get; }

    public int BusyTimeoutMilliseconds { get; }
}
