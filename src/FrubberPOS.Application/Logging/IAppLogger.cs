namespace FrubberPOS.Application.Logging;

/// <summary>
/// Application logging port. The API is synchronous by design: the file
/// sink appends locally and must never be wrapped in <c>Task.Run</c>, nor
/// may callers block the UI thread with <c>.Result</c> or <c>.Wait()</c>
/// (P-DD-005). Dispose once at shutdown to flush buffered entries.
/// </summary>
/// <remarks>
/// Safety contract (ADR-011/016, JHO-15): messages passed here must already
/// be safe. NEVER log PIN, salts, hashes, full license keys, decrypted DPAPI
/// payloads, SQL carrying commercial data, full hardware serials, cart
/// contents or sale lines. Log the event and a diagnostic id; keep the
/// sensitive payload out.
/// </remarks>
public interface IAppLogger : IDisposable
{
    void LogInformation(string eventCode, string message);

    void LogWarning(string eventCode, string message);

    /// <returns>
    /// Diagnostic id (8 uppercase hex chars) written into the entry.
    /// Show this id to the user instead of technical details.
    /// </returns>
    string LogError(string eventCode, string message, Exception? exception = null);
}
