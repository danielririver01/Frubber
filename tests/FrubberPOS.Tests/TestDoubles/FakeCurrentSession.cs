using FrubberPOS.Application.Authentication;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// In-memory session double for tests. Production uses Desktop's
/// <c>CurrentSession</c>.
/// </summary>
public sealed class FakeCurrentSession : ICurrentSession
{
    public SignedInUser? Current { get; private set; }

    public void SignIn(SignedInUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        Current = user;
    }

    public void SignOut() => Current = null;
}
