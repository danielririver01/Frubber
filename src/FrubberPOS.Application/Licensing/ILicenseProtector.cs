namespace FrubberPOS.Application.Licensing;

/// <summary>
/// Seals the activated license for the local user account (DPAPI,
/// CurrentUser scope). Plain payloads never touch disk or logs (JHO-25,
/// ADR-011). Implementations throw <see cref="System.Security.Cryptography.CryptographicException"/>
/// when protected data was tampered with or moved to another account.
/// </summary>
public interface ILicenseProtector
{
    byte[] Protect(byte[] plain);

    byte[] Unprotect(byte[] protectedPayload);
}
