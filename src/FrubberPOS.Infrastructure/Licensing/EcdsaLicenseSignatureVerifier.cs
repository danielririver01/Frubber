using System.Security.Cryptography;
using System.Text;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Domain.Licensing;

namespace FrubberPOS.Infrastructure.Licensing;

/// <summary>
/// Offline license signature check (JHO-25, ADR-020 §16.7): ECDSA P-256 /
/// SHA-256 over the canonical payload, IEEE P1363 signature format.
/// Carries only the owner's public key; unknown key identifiers and any
/// crypto failure verify as <c>false</c> (fail-closed rejection).
/// </summary>
public sealed class EcdsaLicenseSignatureVerifier : ILicenseSignatureVerifier
{
    private readonly byte[] subjectPublicKeyInfo;
    private readonly string keyId;

    public EcdsaLicenseSignatureVerifier(byte[] subjectPublicKeyInfo, string keyId)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        this.subjectPublicKeyInfo = (byte[])subjectPublicKeyInfo.Clone();
        this.keyId = keyId;
    }

    /// <summary>
    /// Production verifier over the embedded owner public key.
    /// </summary>
    public static EcdsaLicenseSignatureVerifier CreateProduction() =>
        new(Convert.FromBase64String(LicensePublicKeys.CurrentPublicKeyBase64), LicensePublicKeys.CurrentKeyId);

    public bool Verify(string keyId, string machineCode, DateOnly issuedDate, byte[] signature)
    {
        if (!string.Equals(keyId, this.keyId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(machineCode)
            || signature is null
            || signature.Length != 64)
        {
            return false;
        }

        try
        {
            var canonical = LicenseKeyFormat.BuildCanonicalPayload(keyId, machineCode, issuedDate);
            var data = Encoding.UTF8.GetBytes(canonical);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
