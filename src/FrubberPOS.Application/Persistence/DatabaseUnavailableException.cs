namespace FrubberPOS.Application.Persistence;

/// <summary>
/// The database engine could not be reached (server down, connection
/// refused, network broken) — JHO-113 · ADR-029. Thrown by the
/// persistence layer at connection time; use cases translate it into a
/// typed failure with <c>ErrorCode.Connection</c> so the caja informs
/// «Sin conexión con el servidor» and no sale is registered, instead of
/// crashing the application.
/// </summary>
public sealed class DatabaseUnavailableException : Exception
{
    /// <summary>Text shown at the caja when the engine is unreachable (JHO-113).</summary>
    public const string UserMessage = "Sin conexión con el servidor";

    public DatabaseUnavailableException(Exception innerException)
        : base(UserMessage, innerException)
    {
    }
}
