using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Users;

/// <summary>
/// Last active administrator guard (P-DD-007, RN-USR): the final active
/// ADMIN can never be deleted, deactivated or demoted. The count is always
/// re-read inside the caller's write transaction — never trusted from the
/// UI. Role authorization itself belongs to JHO-29.
/// </summary>
public static class LastAdminProtection
{
    public static async Task<Error?> CheckAsync(
        IUserRepository users,
        IUnitOfWork unit,
        int targetUserId,
        CancellationToken cancellationToken)
    {
        var target = await users.FindByIdAsync(unit, targetUserId, cancellationToken);
        if (target is null)
        {
            return new Error(ErrorCode.NotFound, "El usuario no existe.");
        }

        if (target.Role == Domain.Users.UserRole.Admin
            && target.IsActive
            && await users.CountActiveAdminsAsync(unit, cancellationToken) <= 1)
        {
            return new Error(ErrorCode.Conflict, "Debe existir al menos un administrador activo.");
        }

        return null;
    }
}
