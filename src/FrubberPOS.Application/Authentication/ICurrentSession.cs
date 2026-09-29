namespace FrubberPOS.Application.Authentication;

/// <summary>
/// In-memory session port (P-DD §10.4). Lives only in RAM, holds no PIN,
/// and is cleared on sign-out. Every protected use case re-checks the role
/// even when the UI hides the option.
/// </summary>
public interface ICurrentSession
{
    SignedInUser? Current { get; }

    void SignIn(SignedInUser user);

    void SignOut();
}
