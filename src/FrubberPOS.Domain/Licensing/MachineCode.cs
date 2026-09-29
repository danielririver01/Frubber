using System.Security.Cryptography;
using System.Text;

namespace FrubberPOS.Domain.Licensing;

/// <summary>
/// Team-bound machine code (JHO-25, ADR-013): SHA-256 over the raw hardware
/// identifiers, reduced to 16 uppercase hex characters. Raw serials never
/// leave Infrastructure; only this code and redacted tokens travel upward.
/// </summary>
public static class MachineCode
{
    public const int Length = 16;

    /// <summary>
    /// Derives the stable code from at least one raw identifier
    /// (SMBIOS UUID, board serial, disk serial, or the documented fallback).
    /// </summary>
    public static string Compute(IReadOnlyList<string> rawIdentifiers)
    {
        if (rawIdentifiers is null || rawIdentifiers.Count == 0 || rawIdentifiers.All(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Se requiere al menos un identificador del equipo.", nameof(rawIdentifiers));
        }

        var joined = string.Join("|", rawIdentifiers.Where(s => !string.IsNullOrWhiteSpace(s)));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    /// <summary>
    /// Copyable display grouping: <c>XXXX-XXXX-XXXX-XXXX</c>.
    /// </summary>
    public static string FormatDisplay(string machineCode)
    {
        if (string.IsNullOrWhiteSpace(machineCode) || machineCode.Length != Length)
        {
            throw new ArgumentException("El código de equipo no tiene el formato esperado.", nameof(machineCode));
        }

        return $"{machineCode[..4]}-{machineCode[4..8]}-{machineCode[8..12]}-{machineCode[12..]}";
    }
}
