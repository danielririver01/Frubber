using System.Security.Cryptography;
using FrubberPOS.Application.Licensing;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// Reversible marker sealing for licensing tests (NOT DPAPI).
/// Production code never uses this.
/// </summary>
public sealed class FakeLicenseProtector : ILicenseProtector
{
    private const byte Marker = 0x46;

    public byte[] Protect(byte[] plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        var sealedPayload = new byte[plain.Length + 1];
        sealedPayload[0] = Marker;
        plain.CopyTo(sealedPayload, 1);
        return sealedPayload;
    }

    public byte[] Unprotect(byte[] protectedPayload)
    {
        ArgumentNullException.ThrowIfNull(protectedPayload);
        if (protectedPayload.Length == 0 || protectedPayload[0] != Marker)
        {
            throw new CryptographicException("Datos protegidos manipulados.");
        }

        return protectedPayload[1..];
    }
}
