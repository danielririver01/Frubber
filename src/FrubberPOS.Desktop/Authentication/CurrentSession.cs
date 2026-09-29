using FrubberPOS.Application.Authentication;

namespace FrubberPOS.Desktop.Authentication;

/// <summary>
/// In-memory session singleton (P-DD §10.4). Holds identity only — never a
/// PIN. Registered once in the Composition Root; use cases receive it
/// through <see cref="ICurrentSession"/>.
/// </summary>
public sealed class CurrentSession : ICurrentSession
{
    private readonly Lock gate = new();
    private SignedInUser? current;

    public SignedInUser? Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    public void SignIn(SignedInUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        lock (gate)
        {
            current = user;
        }
    }

    public void SignOut()
    {
        lock (gate)
        {
            current = null;
        }
    }
}
