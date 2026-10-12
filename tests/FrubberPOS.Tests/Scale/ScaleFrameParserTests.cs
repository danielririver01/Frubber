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
}
