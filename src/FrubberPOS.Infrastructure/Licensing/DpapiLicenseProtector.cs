using System.Runtime.Versioning;
using System.Security.Cryptography;
using FrubberPOS.Application.Licensing;

namespace FrubberPOS.Infrastructure.Licensing;

/// <summary>
/// DPAPI sealing for the activated license (JHO-25, ADR-011): CurrentUser
/// scope, so the sealed blob only opens under the same Windows account on
/// this team. Tampering or account moves surface as
/// <see cref="CryptographicException"/>, which use cases map to unlicensed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiLicenseProtector : ILicenseProtector
{
    public byte[] Protect(byte[] plain)
    {
        if (plain is null || plain.Length == 0)
        {
            throw new ArgumentException("No hay datos para proteger.", nameof(plain));
        }

        return ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] protectedPayload)
    {
        if (protectedPayload is null || protectedPayload.Length == 0)
        {
            throw new ArgumentException("No hay datos protegidos para abrir.", nameof(protectedPayload));
        }

        return ProtectedData.Unprotect(protectedPayload, null, DataProtectionScope.CurrentUser);
    }
}
