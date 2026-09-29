using System.Security.Cryptography;
using FrubberPOS.Application.Security;

namespace FrubberPOS.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA-256 PIN hashing (ADR-011, JHO-24): 16-byte random salt,
/// 32-byte hash, constant-time comparison. Iteration count targets
/// 150–250 ms per verification on minimum hardware; JHO-55 validates it
/// there. Version 1 is the initial algorithm; unknown versions verify as
/// false so old rows fail closed into a plain authentication failure.
/// </summary>
public sealed class Pbkdf2PinHasher : IPinHasher
{
    public const int CurrentAlgorithmVersion = 1;
    public const int SaltSize = 16;
    public const int HashSize = 32;
    public const int DefaultIterations = 210_000;

    public PinHashResult HashPin(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, DefaultIterations, HashAlgorithmName.SHA256, HashSize);
        return new PinHashResult(salt, hash, DefaultIterations, CurrentAlgorithmVersion);
    }

    public bool VerifyPin(string pin, byte[] salt, byte[] hash, int iterations, int algorithmVersion)
    {
        if (pin is null || salt is null || hash is null)
        {
            return false;
        }

        if (algorithmVersion != CurrentAlgorithmVersion
            || salt.Length != SaltSize
            || hash.Length != HashSize
            || iterations < 1)
        {
            return false;
        }

        var candidate = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(candidate, hash);
    }
}
