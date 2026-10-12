using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Application.Users;

public sealed record UpdateUserRequest(int UserId, string DisplayName, UserRole Role);

public sealed record UpdateUserResponse;

/// <summary>
/// Edits name and role (RF-USR-005). Demoting the last active ADMIN to
/// CASHIER is refused with <see cref="ErrorCode.Conflict"/> (P-DD-007).
/// Role authorization belongs to JHO-29.
/// </summary>
public sealed class UpdateUserUseCase : IUseCase<UpdateUserRequest, UpdateUserResponse>
{
    private readonly IUserRepository users;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public UpdateUserUseCase(IUserRepository users, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.users = users;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<UpdateUserResponse>> ExecuteAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<UpdateUserResponse>.Failure(denied);
        }

        var name = request.DisplayName?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 60)
        {
            return Result<UpdateUserResponse>.Failure(new Error(
                ErrorCode.Validation, "El nombre debe tener entre 1 y 60 caracteres.", "DisplayName"));
        }

        // RN-USR-001: only the two approved roles exist.
        if (request.Role is not (UserRole.Admin or UserRole.Cashier))
        {
            return Result<UpdateUserResponse>.Failure(new Error(
                ErrorCode.Validation, "El rol no es válido.", "Role"));
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        var current = await users.FindByIdAsync(unit, request.UserId, cancellationToken);
        if (current is null)
        {
            return Result<UpdateUserResponse>.Failure(new Error(ErrorCode.NotFound, "El usuario no existe."));
        }

        if (await users.DisplayNameExistsAsync(unit, name, request.UserId, cancellationToken))
        {
            return Result<UpdateUserResponse>.Failure(new Error(
                ErrorCode.Conflict, "El nombre ya está en uso.", "DisplayName"));
        }

        if (current.Role == UserRole.Admin && current.IsActive && request.Role == UserRole.Cashier)
        {
            var blocked = await LastAdminProtection.CheckAsync(users, unit, request.UserId, cancellationToken);
            if (blocked is not null)
            {
                return Result<UpdateUserResponse>.Failure(blocked);
            }
        }

        await users.UpdateNameAndRoleAsync(unit, request.UserId, name, request.Role, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<UpdateUserResponse>.Success(new UpdateUserResponse());
    }
}
