using FrubberPOS.Domain.Products;

namespace FrubberPOS.Tests.Products;

/// <summary>
/// Proves the JHO-34 model: PLU/name/price/stock invariants, unit codes,
/// and the thousandths quantity rules per unit.
/// </summary>
public sealed class ProductTests
{
    [Fact]
    public void Create_Valid_SetsDefaults()
    {
        var product = Product.Create("TOM001", "Tomate", ProductUnit.Kg, 2500, 1500);

        Assert.Equal(0, product.ProductId);
        Assert.Equal("TOM001", product.Plu);
        Assert.Equal("Tomate", product.Name);
        Assert.Equal(ProductUnit.Kg, product.Unit);
        Assert.Equal(2500, product.PriceCop);
        Assert.Equal(1500, product.StockMilli);
        Assert.True(product.IsActive);
        Assert.False(product.UnitLocked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tom001")]
    [InlineData("TOM 001")]
    [InlineData("TOM-001")]
    [InlineData("TOM.001")]
    [InlineData(" TOM001")]
    [InlineData("TOM001 ")]
    [InlineData("TOM001Ñ")]
    public void Create_BadPlu_Throws(string? plu)
    {
        Assert.Throws<ArgumentException>(() => Product.Create(plu!, "Tomate", ProductUnit.Kg, 2500, 0));
    }

    [Fact]
    public void Create_PluTooLong_Throws()
    {
        Assert.Throws<ArgumentException>(() => Product.Create(new string('A', 33), "Tomate", ProductUnit.Kg, 2500, 0));
    }

    [Fact]
    public void Create_PluMaxLength_Accepts()
    {
        var product = Product.Create(new string('A', 32), "Tomate", ProductUnit.Kg, 2500, 0);

        Assert.Equal(32, product.Plu.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Tomate")]
    [InlineData("Tomate ")]
    public void Create_BadName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() => Product.Create("TOM001", name!, ProductUnit.Kg, 2500, 0));
    }

    [Fact]
    public void Create_NameTooLong_Throws()
    {
        Assert.Throws<ArgumentException>(() => Product.Create("TOM001", new string('a', 121), ProductUnit.Kg, 2500, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositivePrice_Throws(long price)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Product.Create("TOM001", "Tomate", ProductUnit.Kg, price, 0));
    }

    [Fact]
    public void Create_NegativeStock_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Product.Create("TOM001", "Tomate", ProductUnit.Kg, 2500, -1));
    }

    [Fact]
    public void Rehydrate_KeepsStoredShape()
    {
        var product = Product.Rehydrate(7, "CEB002", "Cebolla", ProductUnit.Unit, 1800, 2000, false, true);

        Assert.Equal(7, product.ProductId);
        Assert.Equal("CEB002", product.Plu);
        Assert.False(product.IsActive);
        Assert.True(product.UnitLocked);
    }

    [Theory]
    [InlineData(ProductUnit.Kg, "KG")]
    [InlineData(ProductUnit.Lb, "LB")]
    [InlineData(ProductUnit.Unit, "UNIT")]
    public void Units_RoundTrip(ProductUnit unit, string code)
    {
        Assert.Equal(code, ProductUnits.ToCode(unit));
        Assert.Equal(unit, ProductUnits.Parse(code));
    }

    [Fact]
    public void Units_UnknownCode_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ProductUnits.Parse("GR"));
    }

    [Theory]
    [InlineData(ProductUnit.Kg, 1, true)]
    [InlineData(ProductUnit.Kg, 1250, true)]
    [InlineData(ProductUnit.Lb, 500, true)]
    [InlineData(ProductUnit.Unit, 1000, true)]
    [InlineData(ProductUnit.Unit, 2000, true)]
    [InlineData(ProductUnit.Unit, 1500, false)]
    [InlineData(ProductUnit.Unit, 1, false)]
    [InlineData(ProductUnit.Kg, 0, false)]
    [InlineData(ProductUnit.Unit, 0, false)]
    [InlineData(ProductUnit.Kg, -1000, false)]
    public void Quantities_ValidatePerUnit(ProductUnit unit, long milli, bool expected)
    {
        Assert.Equal(expected, ProductQuantities.IsValidQuantity(unit, milli));
    }
}
