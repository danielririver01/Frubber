namespace FrubberPOS.Application.Security;

/// <summary>
/// PIN shape shared by every use case that receives one (RF-AUT-002):
/// exactly four numeric digits. Reused by creation, reset and first-admin
/// flows so the rule lives in one place.
/// </summary>
public static class PinRules
{
    public const int Length = 4;

    public static bool IsValidFormat(string? pin) =>
        pin is not null && pin.Length == Length && pin.All(char.IsDigit);

    // Note: char.IsDigit accepts some non-ASCII digits; the login UI only
    // offers 0-9, and the hash treats the string as opaque either way.
}
