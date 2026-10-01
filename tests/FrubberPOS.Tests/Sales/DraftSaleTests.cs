using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Tests.Sales;

public sealed class DraftSaleTests
{
    private static ProductDto KgProduct(int id = 1001) =>
        new(id, "1001", "Manzana Gala", ProductUnit.Kg, 6_000, 18_750, true, true);

    private static ProductDto UnitProduct(int id = 2030) =>
        new(id, "2030", "Leche entera", ProductUnit.Unit, 4_200, 24_000, true, true);

    [Fact]
    public void Add_AccumulatesSameProductInOneLine()
    {
        var draft = new DraftSale();

        Assert.True(draft.AddOrAccumulate(KgProduct(), 1_250).IsSuccess);
        var second = draft.AddOrAccumulate(KgProduct(), 500);

        Assert.True(second.IsSuccess);
        var line = Assert.Single(draft.Lines);
        Assert.Equal(1_750, line.QuantityMilli);
        Assert.Equal(10_500, line.LineTotalCop); // 1.750 kg × $6.000
    }

    [Fact]
    public void Add_InactiveProduct_Validation()
    {
        var draft = new DraftSale();
        var inactive = KgProduct() with { IsActive = false };

        var result = draft.AddOrAccumulate(inactive, 1_000);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Empty(draft.Lines);
    }

    [Fact]
    public void Add_FractionalUnitQuantity_Validation()
    {
        var draft = new DraftSale();

        var result = draft.AddOrAccumulate(UnitProduct(), 1_500);

        Assert.True(result.IsFailure);
        Assert.Empty(draft.Lines);
    }

    [Fact]
    public void Subtotal_LineTotalRoundsHalfUp()
    {
        var draft = new DraftSale();
        draft.AddOrAccumulate(new ProductDto(1, "P1", "Fresa", ProductUnit.Kg, 3_333, 0, true, true), 500);

        Assert.Equal(1_667, draft.SubtotalCop); // 3.333 × 0.500 = 1.666,5 → 1.667
    }

    [Fact]
    public void SetQuantity_Zero_RemovesLine()
    {
        var draft = new DraftSale();
        draft.AddOrAccumulate(KgProduct(), 2_000);

        Assert.True(draft.RemoveOrSetQuantity(1001, 0).IsSuccess);
        Assert.True(draft.IsEmpty);
        Assert.Equal(0, draft.SubtotalCop);
    }

    [Fact]
    public void Clear_EmptiesTheDraft()
    {
        var draft = new DraftSale();
        draft.AddOrAccumulate(KgProduct(), 1_000);
        draft.AddOrAccumulate(UnitProduct(), 2_000);

        draft.Clear();

        Assert.True(draft.IsEmpty);
    }
}
