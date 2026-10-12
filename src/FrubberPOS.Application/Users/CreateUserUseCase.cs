using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Security;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Application.Users;

public sealed record CreateUserRequest(string DisplayName, UserRole Role, string Pin);

public sealed record CreateUserResponse(int UserId);

/// <summary>
/// Creates an active user with a fresh PIN hash (RF-USR-001…004).
/// Role authorization belongs to JHO-29.
/// </summary>
public sealed class CreateUserUseCase : IUseCase<CreateUserRequest, CreateUserResponse>
{
    private readonly IUserRepository users;
    private readonly IPinHasher hasher;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public CreateUserUseCase(IUserRepository users, IPinHasher hasher, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.users = users;
        this.hasher = hasher;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<CreateUserResponse>> ExecuteAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<CreateUserResponse>.Failure(denied);
        }

        var name = request.DisplayName?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 60)
        {
            return Result<CreateUserResponse>.Failure(new Error(
                ErrorCode.Validation, "El nombre debe tener entre 1 y 60 caracteres.", "DisplayName"));
        }

        // RN-USR-001: only the two approved roles exist.
        if (request.Role is not (UserRole.Admin or UserRole.Cashier))
        {
            return Result<CreateUserResponse>.Failure(new Error(
                ErrorCode.Validation, "El rol no es válido.", "Role"));
        }

        if (!PinRules.IsValidFormat(request.Pin))
        {
            return Result<CreateUserResponse>.Failure(new Error(
                ErrorCode.Validation, "El PIN debe ser de 4 dígitos.", "Pin"));
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        if (await users.DisplayNameExistsAsync(unit, name, null, cancellationToken))
        {
            return Result<CreateUserResponse>.Failure(new Error(
                ErrorCode.Conflict, "El nombre ya está en uso.", "DisplayName"));
        }

        var derived = hasher.HashPin(request.Pin);
        var id = await users.CreateAsync(unit, name, request.Role, derived.Salt, derived.Hash, derived.Iterations, derived.AlgorithmVersion, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<CreateUserResponse>.Success(new CreateUserResponse(id));
    }
}
