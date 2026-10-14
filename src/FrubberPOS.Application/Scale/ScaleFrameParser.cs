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
        => TryParse(buffer, 0, count, out reading);

    /// <summary>
    /// Decodes the LATEST complete frame of a capture (JHO-86, verificado en
    /// la báscula real BBG MARKET-30): those frames start with SOH (0x01) —
    /// <c>01 02 "S 01.010kga" 03 04 00</c>. When the capture holds two or
    /// more frames, the reading is marked stable only when the last two
    /// weighings agree: movement is detected without depending on a vendor
    /// stability marker. A truncated trailing frame (read cut mid-number) is
    /// discarded. Buffers without SOH (Torrey/CAS-style streams) fall back
    /// to whole-buffer parsing.
    /// </summary>
    public static bool TryParseLatest(byte[] buffer, int count, out ScaleReading reading)
    {
        reading = new ScaleReading(0, false);
        if (buffer is null || count <= 0)
        {
            return false;
        }

        var frames = new List<ScaleReading>();
        var segmentStart = -1;
        var hasSoh = false;
        for (var i = 0; i < count; i++)
        {
            if (buffer[i] != 0x01)
            {
                continue;
            }

            if (segmentStart >= 0 && i - segmentStart > 1)
            {
                if (TryParse(buffer, segmentStart, i - segmentStart, out var frame))
                {
                    frames.Add(frame);
                }
            }

            segmentStart = i + 1;
            hasSoh = true;
        }

        if (!hasSoh)
        {
            return TryParse(buffer, count, out reading);
        }

        // Último segmento: solo cuenta si trae ETX (trama completa).
        if (segmentStart >= 0 && count - segmentStart > 1
            && global::System.Array.IndexOf(buffer, (byte)0x03, segmentStart, count - segmentStart) >= 0
            && TryParse(buffer, segmentStart, count - segmentStart, out var last))
        {
            frames.Add(last);
        }

        if (frames.Count == 0)
        {
            return false;
        }

        var latest = frames[^1];
        var stable = latest.Stable
            && (frames.Count < 2 || latest.WeightMilli == frames[^2].WeightMilli);
        reading = new ScaleReading(latest.WeightMilli, stable);
        return true;
    }

    private static bool TryParse(byte[] buffer, int offset, int count, out ScaleReading reading)
    {
        var text = Encoding.ASCII.GetString(buffer, offset, count);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c is >= ' ' and <= '~' ? c : ' ');
        }

        return TryParse(sb.ToString(), out reading);
    }
}
