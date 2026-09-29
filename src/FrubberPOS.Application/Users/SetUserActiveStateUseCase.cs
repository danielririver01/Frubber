using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Users;

public sealed record SetUserActiveStateRequest(int UserId, bool IsActive);

public sealed record SetUserActiveStateResponse;

/// <summary>
/// Activates or deactivates a user (RF-USR-006). Deactivating the last
/// active ADMIN is refused (P-DD-007). Inactive users vanish from login
/// (RF-USR-007). Role authorization belongs to JHO-29.
/// </summary>
public sealed class SetUserActiveStateUseCase : IUseCase<SetUserActiveStateRequest, SetUserActiveStateResponse>
{
    private readonly IUserRepository users;
    private readonly IUnitOfWorkFactory units;

    public SetUserActiveStateUseCase(IUserRepository users, IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(units);
        this.users = users;
        this.units = units;
    }

    public async Task<Result<SetUserActiveStateResponse>> ExecuteAsync(SetUserActiveStateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        if (await users.FindByIdAsync(unit, request.UserId, cancellationToken) is null)
        {
            return Result<SetUserActiveStateResponse>.Failure(new Error(ErrorCode.NotFound, "El usuario no existe."));
        }

        if (!request.IsActive)
        {
            var blocked = await LastAdminProtection.CheckAsync(users, unit, request.UserId, cancellationToken);
            if (blocked is not null)
            {
                return Result<SetUserActiveStateResponse>.Failure(blocked);
            }
        }

        await users.SetActiveAsync(unit, request.UserId, request.IsActive, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<SetUserActiveStateResponse>.Success(new SetUserActiveStateResponse());
    }
}
