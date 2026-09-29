using FrubberPOS.Domain.Licensing;

namespace FrubberPOS.Tests.Licensing;

/// <summary>
/// Proves the license text contract the owner's offline generator must match.
/// </summary>
public sealed class LicenseKeyFormatTests
{
    private const string KeyId = "lic-v1-2026-09-29";
    private const string Machine = "0123456789ABCDEF";

    [Fact]
    public void BuildCanonicalPayload_JoinsSchemeParts()
    {
        Assert.Equal(
            "FRB1|lic-v1-2026-09-29|0123456789ABCDEF|20260929",
            LicenseKeyFormat.BuildCanonicalPayload(KeyId, Machine, new DateOnly(2026, 9, 29)));
    }

    [Fact]
    public void TryParse_RoundTripsBuiltPayload()
    {
        var canonical = LicenseKeyFormat.BuildCanonicalPayload(KeyId, Machine, new DateOnly(2026, 9, 29));
        var text = canonical + "." + Convert.ToBase64String(new byte[64]);

        Assert.True(LicenseKeyFormat.TryParse(text, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(KeyId, parsed.KeyId);
        Assert.Equal(Machine, parsed.MachineCode);
        Assert.Equal(new DateOnly(2026, 9, 29), parsed.IssuedDate);
        Assert.Equal(64, parsed.Signature.Length);
    }

    [Fact]
    public void TryParse_TrimsSurroundingWhitespace()
    {
        var canonical = LicenseKeyFormat.BuildCanonicalPayload(KeyId, Machine, new DateOnly(2026, 9, 29));
        Assert.True(LicenseKeyFormat.TryParse("  " + canonical + "." + Convert.ToBase64String(new byte[64]) + "\n", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin-punto")]
    [InlineData("a.b.c")]
    [InlineData("FRB1|solo-dos-partes.firma")]
    [InlineData("OTRO|lic-v1|0123456789ABCDEF|20260929." + SixtyFourZeroBase64)]
    [InlineData("FRB1||0123456789ABCDEF|20260929." + SixtyFourZeroBase64)]
    [InlineData("FRB1|lic-v1|0123456789ABCDE|20260929." + SixtyFourZeroBase64)]
    [InlineData("FRB1|lic-v1|0123456789abcdeZ|20260929." + SixtyFourZeroBase64)]
    [InlineData("FRB1|lic-v1|0123456789ABCDEF|29-09-2026." + SixtyFourZeroBase64)]
    [InlineData("FRB1|lic-v1|0123456789ABCDEF|20261399." + SixtyFourZeroBase64)]
    [InlineData("FRB1|lic-v1|0123456789ABCDEF|20260929.no-es-base64!!!")]
    [InlineData("FRB1|lic-v1|0123456789ABCDEF|20260929.AQID")]
    public void TryParse_RejectsMalformedInput(string? text)
    {
        Assert.False(LicenseKeyFormat.TryParse(text, out var parsed));
        Assert.Null(parsed);
    }

    private const string SixtyFourZeroBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";
}
