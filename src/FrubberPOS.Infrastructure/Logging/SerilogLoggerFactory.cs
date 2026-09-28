using FrubberPOS.Application.Logging;
using Serilog;

namespace FrubberPOS.Infrastructure.Logging;

/// <summary>
/// Builds the application logger: Serilog text files with daily rolling,
/// bounded retention and bounded size (ADR-016).
/// Production directory is <c>%ProgramData%\FrubberPOS\Logs</c>; resolving
/// that well-known path belongs to JHO-22, so callers pass it in.
/// </summary>
public static class SerilogLoggerFactory
{
    public const string FileNamePattern = "log-.txt";

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {EventCode} {DiagnosticId} {Message:lj}{NewLine}{Exception}";

    public static IAppLogger Create(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Directory.CreateDirectory(options.LogDirectory);

        Serilog.ILogger logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(options.LogDirectory, FileNamePattern),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: options.RetainedDayCount,
                fileSizeLimitBytes: options.FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                outputTemplate: OutputTemplate)
            .CreateLogger();

        return new SerilogAppLogger(logger);
    }
}
