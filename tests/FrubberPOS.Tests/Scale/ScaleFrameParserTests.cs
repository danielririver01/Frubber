using FrubberPOS.Application.Scale;

namespace FrubberPOS.Tests.Scale;

/// <summary>
/// Frame decoding for commercial scales (JHO-86): values come as ASCII
/// with the decimals shown on the display; STX/ETX or CR delimited; an
/// optional 'i' marker reports instability (Systel B). No float math:
/// thousandths of the displayed unit (RN-UNI-001/002).
/// </summary>
public sealed class ScaleFrameParserTests
{
    [Theory]
    [InlineData("\u0002 12.340 \u0003", 12_340, true)]    // STX peso ETX (Systel A)
    [InlineData("\r12.340", 12_340, true)]                // Torrey: CR + peso
    [InlineData("\u0002 12.340 \u0003x", 12_340, true)]   // con XOR al final
    [InlineData(" 9.800 ", 9_800, true)]                  // flujo continuo simple
    [InlineData(" 9,800 ", 9_800, true)]                  // coma decimal
    [InlineData(" 0.500 ", 500, true)]
    [InlineData("-0.250 ", -250, true)]                   // peso negativo (tara sucia)
    [InlineData(" 9\u0002 12.340 \u0003", 12_340, true)]  // el último número manda
    [InlineData(" 12.340i x", 12_340, false)]             // Systel B: 'i' = inestable
    [InlineData(" 12.340e x", 12_340, true)]              // Systel B: 'e' = estable
    [InlineData("S 01.010kga", 1_010, true)]              // BBG MARKET-30 con arroz (físico)
    [InlineData("S-00.055kgl", -55, true)]                // BBG vacía sin cero (físico)
    public void TryParse_CommonFrames_DecodesWeight(string frame, long expectedMilli, bool expectedStable)
    {
        var ok = ScaleFrameParser.TryParse(frame, out var reading);

        Assert.True(ok);
        Assert.Equal(expectedMilli, reading.WeightMilli);
        Assert.Equal(expectedStable, reading.Stable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    [InlineData("\u0002\u0003")]
    public void TryParse_EmptyOrGarbage_FailsCleanly(string frame)
    {
        Assert.False(ScaleFrameParser.TryParse(frame, out _));
    }

    [Fact]
    public void TryParse_RawBytes_IgnoresControlCharacters()
    {
        var bytes = new byte[] { 0x02, (byte)' ', (byte)'2', (byte)',', (byte)'0', (byte)'5', (byte)'0', 0x03, 0x7F };

        var ok = ScaleFrameParser.TryParse(bytes, bytes.Length, out var reading);

        Assert.True(ok);
        Assert.Equal(2_050, reading.WeightMilli);
        Assert.True(reading.Stable);
    }

    // ===== Tramas SOH de la BBG MARKET-30 física del dueño (JHO-86) =====

    /// <summary>Arm real: 01 02 + payload + 03 04 00 (capturado al pesar 1.010 kg de arroz).</summary>
    private static byte[] BbgFrame(string payload)
    {
        var p = System.Text.Encoding.ASCII.GetBytes(payload);
        var frame = new byte[p.Length + 5];
        frame[0] = 0x01;
        frame[1] = 0x02;
        System.Buffer.BlockCopy(p, 0, frame, 2, p.Length);
        frame[2 + p.Length] = 0x03;
        frame[3 + p.Length] = 0x04;
        frame[4 + p.Length] = 0x00;
        return frame;
    }

    private static byte[] Join(params byte[][] parts)
    {
        var all = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            System.Buffer.BlockCopy(part, 0, all, offset, part.Length);
            offset += part.Length;
        }

        return all;
    }

    [Fact]
    public void TryParseLatest_TwoIdenticalFrames_IsStable()
    {
        var buffer = Join(BbgFrame("S 01.010kga"), BbgFrame("S 01.010kga"));

        var ok = ScaleFrameParser.TryParseLatest(buffer, buffer.Length, out var reading);

        Assert.True(ok);
        Assert.Equal(1_010, reading.WeightMilli);
        Assert.True(reading.Stable);
    }

    [Fact]
    public void TryParseLatest_ChangingWeight_IsUnstable()
    {
        var buffer = Join(BbgFrame("S 00.950kga"), BbgFrame("S 01.010kga"));

        var ok = ScaleFrameParser.TryParseLatest(buffer, buffer.Length, out var reading);

        Assert.True(ok);
        Assert.Equal(1_010, reading.WeightMilli);
        Assert.False(reading.Stable); // aún se estaba asentando
    }

    [Fact]
    public void TryParseLatest_SingleNegativeFrame_KeepsTheSign()
    {
        var buffer = BbgFrame("S-00.055kgl");

        var ok = ScaleFrameParser.TryParseLatest(buffer, buffer.Length, out var reading);

        Assert.True(ok);
        Assert.Equal(-55, reading.WeightMilli);
    }

    [Fact]
    public void TryParseLatest_TruncatedTrailingFrame_UsesLastCompleteOne()
    {
        // Lectura cortada a mitad de la segunda trama: no debe inventar peso.
        var truncated = new byte[] { 0x01, 0x02, (byte)'S', (byte)' ', (byte)'0', (byte)'1', (byte)'.', (byte)'0' };
        var buffer = Join(BbgFrame("S 01.010kga"), truncated);

        var ok = ScaleFrameParser.TryParseLatest(buffer, buffer.Length, out var reading);

        Assert.True(ok);
        Assert.Equal(1_010, reading.WeightMilli);
    }

    [Fact]
    public void TryParseLatest_BufferWithoutSoh_FallsBackToWholeParsing()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("\r12.340");

        var ok = ScaleFrameParser.TryParseLatest(bytes, bytes.Length, out var reading);

        Assert.True(ok);
        Assert.Equal(12_340, reading.WeightMilli);
    }

    [Fact]
    public void TryParseLatest_OnlyGarbage_FailsCleanly()
    {
        var bytes = new byte[] { 0x01, 0x02, (byte)'-', (byte)'-', 0x03 };

        Assert.False(ScaleFrameParser.TryParseLatest(bytes, bytes.Length, out _));
    }
}
