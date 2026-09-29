using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Security;

namespace FrubberPOS.Application.Users;

public sealed record ResetUserPinRequest(int UserId, string NewPin);

public sealed record ResetUserPinResponse;

/// <summary>
/// Administrator resets any user's PIN with fresh salt and hash; the old
/// value is unrecoverable (RF-USR-004, RF-AUT-007). Role authorization
/// belongs to JHO-29.
/// </summary>
public sealed class ResetUserPinUseCase : IUseCase<ResetUserPinRequest, ResetUserPinResponse>
{
    private readonly IUserRepository users;
    private readonly IPinHasher hasher;
    private readonly IUnitOfWorkFactory units;

    public ResetUserPinUseCase(IUserRepository users, IPinHasher hasher, IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(units);
        this.users = users;
        this.hasher = hasher;
        this.units = units;
    }

    public async Task<Result<ResetUserPinResponse>> ExecuteAsync(ResetUserPinRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!PinRules.IsValidFormat(request.NewPin))
        {
            return Result<ResetUserPinResponse>.Failure(new Error(
                ErrorCode.Validation, "El PIN debe ser de 4 dígitos.", "NewPin"));
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        if (await users.FindByIdAsync(unit, request.UserId, cancellationToken) is null)
        {
            return Result<ResetUserPinResponse>.Failure(new Error(ErrorCode.NotFound, "El usuario no existe."));
        }

        var derived = hasher.HashPin(request.NewPin);
        await users.UpdatePinAsync(unit, request.UserId, derived.Salt, derived.Hash, derived.Iterations, derived.AlgorithmVersion, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<ResetUserPinResponse>.Success(new ResetUserPinResponse());
    }
}
