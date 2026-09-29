namespace FrubberPOS.Infrastructure.Licensing;

/// <summary>
/// Offline license verification keys (JHO-25, ADR-020 §16.7).
/// The application carries ONLY public keys. The private key lives in the
/// owner's offline generator and must never enter this repository.
/// </summary>
public static class LicensePublicKeys
{
    public const string CurrentKeyId = "lic-v1-2026-09-29";

    public const string CurrentAlgorithm = "ECDSA-P256-SHA256";

    /// <summary>
    /// SubjectPublicKeyInfo (DER) Base64 of the owner's offline public key.
    /// Used only to verify license signatures, never to sign.
    /// </summary>
    public const string CurrentPublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAElg9idAnomec0q5VomBxvHamdzBT+ZPSOlLLus30zO+unuDrTiNPoMiE0dRo+XXlWJtcQ7aDxw/mkuSw7xjz6Tg==";
}
