using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Security;
using FrubberPOS.Application.Users;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Application.Startup;

public sealed record CreateFirstAdministratorRequest(string DisplayName, string Pin);

public sealed record CreateFirstAdministratorResponse(int UserId);

/// <summary>
/// First-start flow backend (P-DD-006, JHO-28): enabled only when the User
/// table is empty, creates exactly one active ADMIN in a single
/// transaction. No default PIN exists anywhere: a fresh database holds zero
/// users until this runs. The wizard window and the continuation to normal
/// login ride on JHO-29 navigation; repetition is safe because the second
/// call already sees users.
/// </summary>
public sealed class CreateFirstAdministratorUseCase : IUseCase<CreateFirstAdministratorRequest, CreateFirstAdministratorResponse>
{
    private readonly IUserRepository users;
    private readonly IPinHasher hasher;
    private readonly IUnitOfWorkFactory units;

    public CreateFirstAdministratorUseCase(IUserRepository users, IPinHasher hasher, IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(units);
        this.users = users;
        this.hasher = hasher;
        this.units = units;
    }

    public async Task<Result<CreateFirstAdministratorResponse>> ExecuteAsync(CreateFirstAdministratorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.DisplayName?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 60)
        {
            return Result<CreateFirstAdministratorResponse>.Failure(new Error(
                ErrorCode.Validation, "El nombre debe tener entre 1 y 60 caracteres.", "DisplayName"));
        }

        if (!PinRules.IsValidFormat(request.Pin))
        {
            return Result<CreateFirstAdministratorResponse>.Failure(new Error(
                ErrorCode.Validation, "El PIN debe ser de 4 dígitos.", "Pin"));
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        if (await users.CountAllAsync(unit, cancellationToken) > 0)
        {
            return Result<CreateFirstAdministratorResponse>.Failure(new Error(
                ErrorCode.Conflict, "El inicio inicial ya se completó."));
        }

        var derived = hasher.HashPin(request.Pin);
        var id = await users.CreateAsync(unit, name, UserRole.Admin, derived.Salt, derived.Hash, derived.Iterations, derived.AlgorithmVersion, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<CreateFirstAdministratorResponse>.Success(new CreateFirstAdministratorResponse(id));
    }
}
