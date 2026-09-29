using System.Security.Cryptography;
using FrubberPOS.Infrastructure.Licensing;

namespace FrubberPOS.Tests.Licensing;

/// <summary>
/// Proves DPAPI sealing round-trips and fails closed on tampering.
/// Windows-only, like the product.
/// </summary>
public sealed class DpapiLicenseProtectorTests
{
    [Fact]
    public void ProtectUnprotect_RoundTrips()
    {
        var protector = new DpapiLicenseProtector();
        var plain = new byte[] { 1, 2, 3, 4 };

        var sealedPayload = protector.Protect(plain);

        Assert.NotEqual(plain, sealedPayload);
        Assert.Equal(plain, protector.Unprotect(sealedPayload));
    }

    [Fact]
    public void Protect_Empty_Throws()
    {
        var protector = new DpapiLicenseProtector();

        Assert.Throws<ArgumentException>(() => protector.Protect([]));
    }

    [Fact]
    public void Unprotect_Empty_Throws()
    {
        var protector = new DpapiLicenseProtector();

        Assert.Throws<ArgumentException>(() => protector.Unprotect([]));
    }

    [Fact]
    public void Unprotect_Tampered_ThrowsCryptographic()
    {
        var protector = new DpapiLicenseProtector();
        var sealedPayload = protector.Protect([9, 9, 9]);
        sealedPayload[^1] ^= 0xFF;

        Assert.Throws<CryptographicException>(() => protector.Unprotect(sealedPayload));
    }
}
