using System.Text;

namespace FrubberPOS.Application.Scale;

/// <summary>
/// Decodes a weighing frame from a commercial scale (JHO-86). Commercial
/// scales in this market (BBG, Systel, Torrey-compatible, CAS-compatible)
/// transmit the displayed value as ASCII, enclosed in STX/ETX or
/// CR-terminated frames, optionally with a stability marker ('e'/'i').
/// The parser is deliberately tolerant: it extracts the last numeric token
/// of the frame and, when present, the stability marker. The value keeps
/// the decimals shown on the display and is returned in thousandths
/// (kg/lb × 1000) — no float, no unit conversion (RN-UNI-005).
/// </summary>
public static class ScaleFrameParser
{
    /// <summary>Extracts a reading from a raw frame; false when no number is readable.</summary>
    public static bool TryParse(string frame, out ScaleReading reading)
    {
        reading = new ScaleReading(0, false);
        if (string.IsNullOrWhiteSpace(frame))
        {
            return false;
        }

        var stable = true;
        var text = frame;
        var iMarker = text.IndexOf('i');
        if (iMarker > 0 && char.IsDigit(text[iMarker - 1]))
        {
            stable = false; // Systel protocolo B: 'i' = inestable
        }

        // Último token numérico del cuadro: signo opcional, dígitos, decimales . o ,
        global::System.Text.RegularExpressions.Match? match = null;
        foreach (global::System.Text.RegularExpressions.Match m in
                 global::System.Text.RegularExpressions.Regex.Matches(text, @"-?\d+(?:[.,]\d+)?"))
        {
            match = m;
        }

        if (match is null)
        {
            return false;
        }

        var token = match.Value;
        var negative = token.StartsWith('-');
        var digits = negative ? token[1..] : token;
        var sep = digits.IndexOfAny(['.', ',']);
        long whole = long.Parse(sep < 0 ? digits : digits[..sep]);
        long milli = 0;
        if (sep >= 0)
        {
            var frac = digits[(sep + 1)..];
            frac = frac.Length > 3 ? frac[..3] : frac.PadRight(3, '0');
            milli = long.Parse(frac);
        }

        var total = whole * 1_000 + milli;
        reading = new ScaleReading(negative ? -total : total, stable);
        return true;
    }

    /// <summary>Decodes raw bytes as ASCII, dropping control characters before parsing.</summary>
    public static bool TryParse(byte[] buffer, int count, out ScaleReading reading)
    {
        var text = Encoding.ASCII.GetString(buffer, 0, count);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c is >= ' ' and <= '~' ? c : ' ');
        }

        return TryParse(sb.ToString(), out reading);
    }
}
