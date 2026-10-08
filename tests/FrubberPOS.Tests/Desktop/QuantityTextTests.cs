using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Tests.Desktop;

public sealed class QuantityTextTests
{
    [Theory]
    [InlineData("2.5", 2500)]
    [InlineData("2,5", 2500)]
    [InlineData("3", 3000)]
    [InlineData("0.001", 1)]
    [InlineData("-1.25", -1250)]
    [InlineData(" 4 ", 4000)]
    public void TryParseToMilli_WeightUnit_Parses(string text, long expected)
    {
        Assert.True(QuantityText.TryParseToMilli(text, ProductUnit.Kg, out var milli));
        Assert.Equal(expected, milli);
    }

    [Theory]
    [InlineData("3", 3000)]
    [InlineData("-2", -2000)]
    [InlineData("0", 0)]
    public void TryParseToMilli_Unit_ParsesWholeOnly(string text, long expected)
    {
        Assert.True(QuantityText.TryParseToMilli(text, ProductUnit.Unit, out var milli));
        Assert.Equal(expected, milli);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("1.2345")]
    [InlineData(".5")]
    [InlineData("-")]
    public void TryParseToMilli_InvalidWeightText_Rejected(string text)
    {
        Assert.False(QuantityText.TryParseToMilli(text, ProductUnit.Kg, out _));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1,0")]
    [InlineData("0.000")]
    public void TryParseToMilli_UnitRejectsFractions(string text)
    {
        Assert.False(QuantityText.TryParseToMilli(text, ProductUnit.Unit, out _));
    }

    [Theory]
    [InlineData(2500, "2,5")]
    [InlineData(3000, "3")]
    [InlineData(1, "0,001")]
    [InlineData(-1250, "-1,25")]
    [InlineData(0, "0")]
    public void Format_WeightUnit(long milli, string expected)
    {
        Assert.Equal(expected, QuantityText.Format(milli, ProductUnit.Kg));
    }

    [Theory]
    [InlineData(3000, "3")]
    [InlineData(-2000, "-2")]
    public void Format_UnitShowsWhole(long milli, string expected)
    {
        Assert.Equal(expected, QuantityText.Format(milli, ProductUnit.Unit));
    }

    [Theory]
    [InlineData(1350)]
    [InlineData(-1250)]
    [InlineData(1)]
    public void Format_ThenParse_RoundTripsWithComma(long milli)
    {
        // JHO-67 N4: el texto mostrado con coma vuelve a parsear igual.
        var text = QuantityText.Format(milli, ProductUnit.Kg);

        Assert.True(QuantityText.TryParseToMilli(text, ProductUnit.Kg, out var parsed));
        Assert.Equal(milli, parsed);
    }
}
