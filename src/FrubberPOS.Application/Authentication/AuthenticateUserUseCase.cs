using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Security;
using FrubberPOS.Application.Users;

namespace FrubberPOS.Application.Authentication;

public sealed record AuthenticateUserRequest(int UserId, string Pin);

public sealed record AuthenticateUserResponse(int UserId, string DisplayName, FrubberPOS.Domain.Users.UserRole Role);

/// <summary>
/// Verifies the PIN with constant-time comparison and creates the
/// in-memory session (RF-AUT-001…005, RN-USR-002). Unknown user, inactive
/// user and wrong PIN all return the same message: no user enumeration.
/// The entered PIN is never stored, logged or returned; failures carry no
/// technical details.
/// </summary>
public sealed class AuthenticateUserUseCase : IUseCase<AuthenticateUserRequest, AuthenticateUserResponse>
{
    private const string InvalidCredentialsMessage = "Usuario o PIN inválidos.";

    private readonly IUserRepository users;
    private readonly IPinHasher hasher;
    private readonly ICurrentSession session;
    private readonly IUnitOfWorkFactory units;

    public AuthenticateUserUseCase(IUserRepository users, IPinHasher hasher, ICurrentSession session, IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(units);
        this.users = users;
        this.hasher = hasher;
        this.session = session;
        this.units = units;
    }

    public async Task<Result<AuthenticateUserResponse>> ExecuteAsync(AuthenticateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!PinRules.IsValidFormat(request.Pin))
        {
            return Result<AuthenticateUserResponse>.Failure(new Error(
                ErrorCode.Validation, "El PIN debe ser de 4 dígitos.", "Pin"));
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var credentials = await users.FindCredentialsByIdAsync(unit, request.UserId, cancellationToken);
        if (credentials is null || !credentials.IsActive)
        {
            return Result<AuthenticateUserResponse>.Failure(new Error(ErrorCode.Authentication, InvalidCredentialsMessage));
        }

        if (!hasher.VerifyPin(request.Pin, credentials.PinSalt, credentials.PinHash, credentials.PinIterations, credentials.PinAlgorithmVersion))
        {
            return Result<AuthenticateUserResponse>.Failure(new Error(ErrorCode.Authentication, InvalidCredentialsMessage));
        }

        var signedIn = new SignedInUser(credentials.UserId, credentials.DisplayName, credentials.Role);
        session.SignIn(signedIn);
        return Result<AuthenticateUserResponse>.Success(new AuthenticateUserResponse(signedIn.UserId, signedIn.DisplayName, signedIn.Role));
    }
}
