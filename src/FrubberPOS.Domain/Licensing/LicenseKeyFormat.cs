using System.Globalization;

namespace FrubberPOS.Domain.Licensing;

/// <summary>
/// A license key already split into its parts. The signature covers the
/// canonical payload built by <see cref="LicenseKeyFormat"/> and is verified
/// with ECDSA P-256 / SHA-256 in IEEE P1363 format (JHO-25).
/// </summary>
public sealed record ParsedLicenseKey(string KeyId, string MachineCode, DateOnly IssuedDate, byte[] Signature);

/// <summary>
/// Offline lifetime-license text format (JHO-25, ADR-020 §16.7).
/// Customer-facing text: <c>FRB1|keyId|MACHINECODE|yyyyMMdd.base64Signature</c>.
/// Pure parsing and canonical building: no crypto, no I/O, so the owner's
/// offline generator can implement this exact contract independently.
/// </summary>
public static class LicenseKeyFormat
{
    public const string Scheme = "FRB1";

    public const int MachineCodeLength = 16;

    /// <summary>
    /// Exact bytes (ASCII/UTF-8) covered by the license signature.
    /// </summary>
    public static string BuildCanonicalPayload(string keyId, string machineCode, DateOnly issuedDate) =>
        $"{Scheme}|{keyId}|{machineCode}|{issuedDate:yyyyMMdd}";

    /// <summary>
    /// Parses customer input without throwing: trims whitespace and fails
    /// closed (false) on any shape, Base64 or date problem.
    /// </summary>
    public static bool TryParse(string? text, out ParsedLicenseKey? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        var payloadParts = parts[0].Split('|');
        if (payloadParts.Length != 4 || payloadParts[0] != Scheme)
        {
            return false;
        }

        var keyId = payloadParts[1];
        var machineCode = payloadParts[2];
        if (string.IsNullOrWhiteSpace(keyId) || !IsMachineCodeShape(machineCode))
        {
            return false;
        }

        if (!DateOnly.TryParseExact(payloadParts[3], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var issuedDate))
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (signature.Length != 64)
        {
            return false;
        }

        parsed = new ParsedLicenseKey(keyId, machineCode, issuedDate, signature);
        return true;
    }

    private static bool IsMachineCodeShape(string machineCode) =>
        machineCode.Length == MachineCodeLength && machineCode.All(c => IsUpperHex(c));

    private static bool IsUpperHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
}
