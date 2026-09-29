using FrubberPOS.Application.Common;

namespace FrubberPOS.Application.Authentication;

public sealed record SignOutRequest;

public sealed record SignOutResponse;

/// <summary>
/// Clears the in-memory session and returns to the user selector.
/// </summary>
public sealed class SignOutUseCase : IUseCase<SignOutRequest, SignOutResponse>
{
    private readonly ICurrentSession session;

    public SignOutUseCase(ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        this.session = session;
    }

    public Task<Result<SignOutResponse>> ExecuteAsync(SignOutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        session.SignOut();
        return Task.FromResult(Result<SignOutResponse>.Success(new SignOutResponse()));
    }
}
