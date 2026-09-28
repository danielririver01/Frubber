namespace FrubberPOS.Application.Logging;

/// <summary>
/// File logging policy (ADR-016): daily rolling files, bounded retention
/// and bounded size so a faulty loop can never fill the disk.
/// The concrete directory is supplied by the caller. In production it is
/// <c>%ProgramData%\FrubberPOS\Logs</c>; wiring that well-known path
/// belongs to JHO-22, not here.
/// </summary>
public sealed record LoggingOptions
{
    public LoggingOptions(string logDirectory, int retainedDayCount = 15, long fileSizeLimitBytes = 10 * 1024 * 1024)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("El directorio de logs no puede estar vacío.", nameof(logDirectory));
        }

        if (retainedDayCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedDayCount), "La retención debe ser de al menos un día.");
        }

        if (fileSizeLimitBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSizeLimitBytes), "El tamaño máximo de archivo debe ser positivo.");
        }

        LogDirectory = logDirectory;
        RetainedDayCount = retainedDayCount;
        FileSizeLimitBytes = fileSizeLimitBytes;
    }

    public string LogDirectory { get; }

    public int RetainedDayCount { get; }

    public long FileSizeLimitBytes { get; }
}
