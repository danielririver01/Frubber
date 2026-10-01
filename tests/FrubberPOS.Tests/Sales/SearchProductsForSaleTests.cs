using Dapper;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Sales;

public sealed class SearchProductsForSaleTests
{
    private static SearchProductsForSaleUseCase UseCase(ProductTestBed bed) =>
        new(bed.Products, bed.Units, ProductTestBed.AdminSession());

    [Fact]
    public async Task Search_OnlyActive_ExactPluFirst()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("1001", "Manzana Gala", Domain.Products.ProductUnit.Kg, 6_000, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("1001A", "Manzana Verde", Domain.Products.ProductUnit.Kg, 5_800, 0), CancellationToken.None);
        var inactive = await create.ExecuteAsync(new CreateProductRequest("B1", "Banano", Domain.Products.ProductUnit.Unit, 500, 0), CancellationToken.None);
        await new SetProductActiveStateUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new SetProductActiveStateRequest(inactive.Value.ProductId, false), CancellationToken.None);

        var search = UseCase(bed);
        var exact = await search.ExecuteAsync(new SearchForSaleRequest("1001"), CancellationToken.None);

        Assert.True(exact.IsSuccess);
        Assert.Equal("1001", exact.Value.Items[0].Plu);
        Assert.DoesNotContain(exact.Value.Items, i => i.Plu == "B1");

        // Nombre muestra PLU+unidad+precio+existencia.
        var item = exact.Value.Items.First(i => i.Plu == "1001");
        Assert.Equal("Manzana Gala", item.Name);
        Assert.Equal("KG", item.UnitCode);
        Assert.Equal(6_000, item.PriceCop);
        Assert.Equal(0, item.StockMilli);
    }

    [Fact]
    public async Task Search_NameMatch_IncludesResult()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("1001", "Manzana Gala", Domain.Products.ProductUnit.Kg, 6_000, 0), CancellationToken.None);

        var search = UseCase(bed);
        var byName = await search.ExecuteAsync(new SearchForSaleRequest("gala"), CancellationToken.None);

        Assert.Single(byName.Value.Items);
        Assert.Equal("1001", byName.Value.Items[0].Plu);
    }

    [Fact]
    public async Task Search_WithoutSession_Authorization()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var search = new SearchProductsForSaleUseCase(bed.Products, bed.Units, new FakeCurrentSession());

        var result = await search.ExecuteAsync(new SearchForSaleRequest(""), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }
}
