using System.Security.Cryptography;
using System.Text;

namespace FrubberPOS.Application.System;

/// <summary>
/// Safety policy for physical hardware identifiers (ADR-013, JHO-22):
/// full serials never reach UI or logs. This helper turns a raw serial
/// into a stable redacted token (partial mask plus hash). Reading the raw
/// identifiers (SMBIOS UUID, board and disk serials) belongs to JHO-25.
/// </summary>
public static class MachineIdentityRedactor
{
    public static string Redact(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("El identificador no puede estar vacío.", nameof(serial));
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serial)))[..8];
        var mask = serial.Length <= 4 ? "****" : $"{serial[..2]}**{serial[^2..]}";
        return $"{mask}:{hash}";
    }
}
