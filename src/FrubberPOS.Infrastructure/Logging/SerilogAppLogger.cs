using FrubberPOS.Application.Logging;

namespace FrubberPOS.Infrastructure.Logging;

internal sealed class SerilogAppLogger : IAppLogger
{
    private readonly Serilog.ILogger logger;
    private bool disposed;

    internal SerilogAppLogger(Serilog.ILogger logger)
    {
        this.logger = logger;
    }

    public void LogInformation(string eventCode, string message)
    {
        RequireValid(eventCode, message);
        logger.ForContext("EventCode", eventCode).Information(message);
    }

    public void LogWarning(string eventCode, string message)
    {
        RequireValid(eventCode, message);
        logger.ForContext("EventCode", eventCode).Warning(message);
    }

    public string LogError(string eventCode, string message, Exception? exception = null)
    {
        RequireValid(eventCode, message);
        var diagnosticId = Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8];
        logger.ForContext("EventCode", eventCode)
            .ForContext("DiagnosticId", diagnosticId)
            .Error(exception, message);
        return diagnosticId;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        (logger as IDisposable)?.Dispose();
    }

    private void RequireValid(string eventCode, string message)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (string.IsNullOrWhiteSpace(eventCode))
        {
            throw new ArgumentException("El código de evento no puede estar vacío.", nameof(eventCode));
        }

        ArgumentNullException.ThrowIfNull(message);
    }
}
