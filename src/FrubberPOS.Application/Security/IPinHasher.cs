namespace FrubberPOS.Application.Security;

/// <summary>
/// PIN hashing port (ADR-011, P-DD §11.1). Implementations never return,
/// log or expose the PIN itself. Unknown algorithm versions verify as
/// <c>false</c> instead of throwing, so callers map them to a plain
/// authentication failure.
/// </summary>
public interface IPinHasher
{
    PinHashResult HashPin(string pin);

    bool VerifyPin(string pin, byte[] salt, byte[] hash, int iterations, int algorithmVersion);
}
