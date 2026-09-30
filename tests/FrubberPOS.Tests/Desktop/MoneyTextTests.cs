using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Tests.Desktop;

public sealed class MoneyTextTests
{
    [Theory]
    [InlineData(6000, "$ 6.000")]
    [InlineData(0, "$ 0")]
    [InlineData(18450, "$ 18.450")]
    [InlineData(-2550, "$ -2.550")]
    public void FormatCop_GroupsThousands(long cop, string expected)
    {
        Assert.Equal(expected, MoneyText.FormatCop(cop));
    }

    [Theory]
    [InlineData("6000", 6000)]
    [InlineData("6.000", 6000)]
    [InlineData("$ 6.000", 6000)]
    public void TryParseCop_AcceptsGroupedAndPlain(string text, long expected)
    {
        Assert.True(MoneyText.TryParseCop(text, out var cop));
        Assert.Equal(expected, cop);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12-")]
    public void TryParseCop_RejectsInvalid(string text)
    {
        Assert.False(MoneyText.TryParseCop(text, out _));
    }
}

public sealed class ProductRowViewModelTests
{
    [Fact]
    public void NegativeStock_CarriesSignColorFlagAndAccessibleText()
    {
        var row = ProductRowViewModel.From(
            new ProductDto(1, "M1", "Mango", ProductUnit.Kg, 6000, -1250, true, true, false));

        Assert.Equal("-1.25 KG", row.StockText);
        Assert.True(row.IsNegativeStock);
        Assert.Contains("Existencia negativa", row.StockA11yText);
    }

    [Fact]
    public void PositiveStock_NoNegativeMarker()
    {
        var row = ProductRowViewModel.From(
            new ProductDto(1, "M1", "Mango", ProductUnit.Unit, 6000, 4000, true, true, false));

        Assert.Equal("4 UNIT", row.StockText);
        Assert.False(row.IsNegativeStock);
        Assert.DoesNotContain("negativa", row.StockA11yText);
        Assert.Equal("$ 6.000", row.PriceText);
    }
}
