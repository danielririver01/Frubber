using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Users;

public sealed record DeleteUserRequest(int UserId);

public sealed record DeleteUserResponse;

/// <summary>
/// Permanent deletion only when the user has no sales, cash sessions,
/// voids or other history (RF-USR-008, RB-020); otherwise
/// <see cref="ErrorCode.Conflict"/> directing to deactivate instead.
/// Deleting the last active ADMIN is refused (P-DD-007).
/// Role authorization belongs to JHO-29.
/// </summary>
public sealed class DeleteUserUseCase : IUseCase<DeleteUserRequest, DeleteUserResponse>
{
    private readonly IUserRepository users;
    private readonly IUnitOfWorkFactory units;

    public DeleteUserUseCase(IUserRepository users, IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(units);
        this.users = users;
        this.units = units;
    }

    public async Task<Result<DeleteUserResponse>> ExecuteAsync(DeleteUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        if (await users.FindByIdAsync(unit, request.UserId, cancellationToken) is null)
        {
            return Result<DeleteUserResponse>.Failure(new Error(ErrorCode.NotFound, "El usuario no existe."));
        }

        var blocked = await LastAdminProtection.CheckAsync(users, unit, request.UserId, cancellationToken);
        if (blocked is not null)
        {
            return Result<DeleteUserResponse>.Failure(blocked);
        }

        if (await users.HasHistoryAsync(unit, request.UserId, cancellationToken))
        {
            return Result<DeleteUserResponse>.Failure(new Error(
                ErrorCode.Conflict, "El usuario tiene historial y no puede eliminarse. Desactívelo en su lugar."));
        }

        await users.DeleteAsync(unit, request.UserId, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<DeleteUserResponse>.Success(new DeleteUserResponse());
    }
}
