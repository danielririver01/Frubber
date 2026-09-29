using System.Security.Cryptography;
using System.Text;
using FrubberPOS.Domain.Licensing;
using FrubberPOS.Infrastructure.Licensing;

namespace FrubberPOS.Tests.Licensing;

/// <summary>
/// Proves the ECDSA verifier accepts genuine signatures and rejects
/// anything else without throwing, including the embedded production key.
/// </summary>
public sealed class EcdsaLicenseSignatureVerifierTests
{
    private const string KeyId = "test-key";
    private const string Machine = "0123456789ABCDEF";

    [Fact]
    public void Verify_GenuineSignature_ReturnsTrue()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new EcdsaLicenseSignatureVerifier(ecdsa.ExportSubjectPublicKeyInfo(), KeyId);
        var date = new DateOnly(2026, 9, 29);

        var signature = Sign(ecdsa, KeyId, Machine, date);

        Assert.True(verifier.Verify(KeyId, Machine, date, signature));
    }

    [Fact]
    public void Verify_WrongMachine_ReturnsFalse()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new EcdsaLicenseSignatureVerifier(ecdsa.ExportSubjectPublicKeyInfo(), KeyId);
        var date = new DateOnly(2026, 9, 29);

        var signature = Sign(ecdsa, KeyId, Machine, date);

        Assert.False(verifier.Verify(KeyId, "FFFFFFFFFFFFFFFF", date, signature));
    }

    [Fact]
    public void Verify_TamperedSignature_ReturnsFalse()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new EcdsaLicenseSignatureVerifier(ecdsa.ExportSubjectPublicKeyInfo(), KeyId);
        var date = new DateOnly(2026, 9, 29);

        var signature = Sign(ecdsa, KeyId, Machine, date);
        signature[0] ^= 0xFF;

        Assert.False(verifier.Verify(KeyId, Machine, date, signature));
    }

    [Fact]
    public void Verify_UnknownKeyId_ReturnsFalse()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new EcdsaLicenseSignatureVerifier(ecdsa.ExportSubjectPublicKeyInfo(), KeyId);

        Assert.False(verifier.Verify("other-key", Machine, new DateOnly(2026, 9, 29), new byte[64]));
    }

    [Fact]
    public void Verify_ShortSignature_ReturnsFalse()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new EcdsaLicenseSignatureVerifier(ecdsa.ExportSubjectPublicKeyInfo(), KeyId);

        Assert.False(verifier.Verify(KeyId, Machine, new DateOnly(2026, 9, 29), [1, 2, 3]));
    }

    [Fact]
    public void CreateProduction_ImportsEmbeddedKeyAndFailsClosed()
    {
        var verifier = EcdsaLicenseSignatureVerifier.CreateProduction();

        Assert.False(verifier.Verify("unknown", Machine, new DateOnly(2026, 9, 29), new byte[64]));
        Assert.False(verifier.Verify(LicensePublicKeys.CurrentKeyId, Machine, new DateOnly(2026, 9, 29), new byte[64]));
    }

    private static byte[] Sign(ECDsa ecdsa, string keyId, string machine, DateOnly date)
    {
        var data = Encoding.UTF8.GetBytes(LicenseKeyFormat.BuildCanonicalPayload(keyId, machine, date));
        return ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
