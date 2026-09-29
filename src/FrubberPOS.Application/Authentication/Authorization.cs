using FrubberPOS.Application.Common;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Application.Authentication;

/// <summary>
/// Role enforcement inside use cases (JHO-29). Hiding buttons is UX only;
/// every protected use case calls <see cref="RequireAdmin"/> before
/// touching ports. History and void follow the same pattern when they
/// arrive (Fase 7).
/// </summary>
public static class Authorization
{
    public const string DeniedMessage = "No tiene permiso para esta operación.";

    /// <returns><c>null</c> when the current session is an ADMIN, else the denial error.</returns>
    public static Error? RequireAdmin(ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.Current?.Role == UserRole.Admin
            ? null
            : new Error(ErrorCode.Authorization, DeniedMessage);
    }
}
