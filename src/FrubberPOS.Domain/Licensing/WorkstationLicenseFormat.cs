using System.Globalization;

namespace FrubberPOS.Domain.Licensing;

public sealed record ParsedWorkstationLicense(ParsedLicenseKey Key, int Seats, string CanonicalPayload);

public static class WorkstationLicenseFormat
{
    public static bool TryParse(string? text, out ParsedWorkstationLicense? license)
    {
        license = null;
        if (LicenseKeyFormat.TryParse(text, out var legacy))
        {
            var key = legacy!;
            license = new(key, 1, LicenseKeyFormat.BuildCanonicalPayload(key.KeyId, key.MachineCode, key.IssuedDate));
            return true;
        }

        var parts = text?.Trim().Split('.');
        if (parts is not { Length: 2 })
        {
            return false;
        }

        var fields = parts[0].Split('|');
        if (fields is not ["FRB2", var keyId, var machineCode, var date, var seatText] ||
            !int.TryParse(seatText, NumberStyles.None, CultureInfo.InvariantCulture, out var seats) ||
            seats < 1 || seatText != seats.ToString(CultureInfo.InvariantCulture) ||
            !LicenseKeyFormat.TryParse($"FRB1|{keyId}|{machineCode}|{date}.{parts[1]}", out var parsed))
        {
            return false;
        }

        license = new(parsed!, seats, parts[0]);
        return true;
    }
}
