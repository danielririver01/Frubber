using System.Globalization;

namespace FrubberPOS.Desktop.Common;

/// <summary>
/// Whole-COP money formatting (ADR-009): "$ 6.000" with the es-CO group
/// separator. Parsing accepts digits and group separators only.
/// </summary>
public static class MoneyText
{
    public static string FormatCop(long cop) =>
        "$ " + cop.ToString("N0", CultureInfo.GetCultureInfo("es-CO"));

    public static bool TryParseCop(string? text, out long cop)
    {
        cop = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var digits = text.Trim().Replace(".", string.Empty).Replace(" ", string.Empty);
        if (digits.StartsWith('$'))
        {
            digits = digits[1..].Trim();
        }

        if (digits.Length is 0 or > 15 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        return long.TryParse(digits, out cop);
    }
}
