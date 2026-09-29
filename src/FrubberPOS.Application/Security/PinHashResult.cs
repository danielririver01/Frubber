namespace FrubberPOS.Application.Security;

/// <summary>
/// Derived PIN material: 16-byte salt, 32-byte PBKDF2-HMAC-SHA-256 hash,
/// iteration count and algorithm version (ADR-011). Never logged.
/// </summary>
public sealed record PinHashResult(byte[] Salt, byte[] Hash, int Iterations, int AlgorithmVersion);
