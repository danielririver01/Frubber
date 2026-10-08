using FrubberPOS.Domain.Products;

namespace FrubberPOS.Desktop.Common;

/// <summary>
/// Text &lt;-&gt; thousandths conversion for quantity inputs (JHO-33).
/// Never uses floating point (ADR-009): the fractional part is padded and
/// parsed as integer digits. Accepts '.' or ',' as separator; UNIT
/// quantities accept whole numbers only (RN-INV-006).
/// </summary>
public static class QuantityText
{
    public static bool TryParseToMilli(string? text, ProductUnit unit, out long milli)
    {
        milli = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim().Replace(',', '.');
        var negative = t.StartsWith('-');
        if (negative)
        {
            t = t[1..];
        }

        var parts = t.Split('.');
        if (parts.Length > 2
            || parts[0].Length is 0 or > 15
            || !parts[0].All(char.IsAsciiDigit)
            || (parts.Length == 2 && (unit == ProductUnit.Unit || parts[1].Length is 0 or > 3 || !parts[1].All(char.IsAsciiDigit))))
        {
            return false;
        }

        if (!long.TryParse(parts[0], out var integerPart))
        {
            return false;
        }

        var fractionMilli = 0L;
        if (parts.Length == 2)
        {
            fractionMilli = long.Parse(parts[1].PadRight(3, '0'));
        }

        milli = integerPart * ProductQuantities.Scale + fractionMilli;
        if (negative)
        {
            milli = -milli;
        }

        return true;
    }

    /// <summary>
    /// Display (JHO-67 N4): es-CO shows fractions with a decimal comma
    /// ("1,35"); <see cref="TryParseToMilli"/> still accepts both separators.
    /// </summary>
    public static string Format(long milli, ProductUnit unit)
    {
        if (unit == ProductUnit.Unit)
        {
            return (milli / ProductQuantities.Scale).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var negative = milli < 0;
        var absolute = negative ? -milli : milli;
        var integerPart = absolute / ProductQuantities.Scale;
        var fraction = (absolute % ProductQuantities.Scale).ToString("000").TrimEnd('0');
        var text = fraction.Length == 0
            ? integerPart.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : integerPart.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + fraction;
        return negative ? "-" + text : text;
    }
}
