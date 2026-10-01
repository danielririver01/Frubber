using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Sales;

public sealed class TpvViewModelTests
{
    private static TpvViewModel Build(ProductTestBed bed) =>
        new(new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()));

    [Fact]
    public async Task Search_PopulatesResultsWithActiveOnly()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 4_500, 0), CancellationToken.None);
        var inactive = await create.ExecuteAsync(new CreateProductRequest("M2", "Mango Criollo", Domain.Products.ProductUnit.Kg, 4_000, 0), CancellationToken.None);
        await new SetProductActiveStateUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new SetProductActiveStateRequest(inactive.Value.ProductId, false), CancellationToken.None);

        var vm = Build(bed);
        await vm.SearchAsync();

        Assert.Single(vm.Results);
        Assert.Equal("M1", vm.Results[0].Plu);
    }

    [Fact]
    public async Task AddSelected_AddsLineToDraft()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var vm = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();

        await vm.AddSelectedAsync();

        Assert.Single(vm.Lines);
        Assert.Equal("$ 4.200", vm.SubtotalText);
    }

    [Fact]
    public async Task AddSameProduct_AccumulatesOneLine()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var vm = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();

        await vm.AddSelectedAsync();
        await vm.AddSelectedAsync();

        Assert.Single(vm.Lines);
        Assert.Equal(2_000, vm.Lines[0].QuantityMilli);
    }

    [Fact]
    public async Task Search_ReplacedQuery_NewResultsWin()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 4_500, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var vm = Build(bed);
        vm.SearchText = "mango";
        await vm.SearchAsync();

        Assert.Single(vm.Results);
        Assert.Equal("M1", vm.Results[0].Plu);

        vm.SearchText = "leche";
        await vm.SearchAsync();

        Assert.Single(vm.Results);
        Assert.Equal("L1", vm.Results[0].Plu);
    }

    [Fact]
    public async Task RequestFocusSearch_FiresEvent()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var vm = Build(bed);
        var fired = 0;
        vm.FocusSearchRequested += (_, _) => fired++;

        vm.RequestFocusSearch();

        Assert.Equal(1, fired);
        await vm.SearchAsync(); // no deforma estado
    }

    [Fact]
    public async Task ClearDraft_EmptiesLines()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var vm = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        await vm.ClearDraft();

        Assert.Empty(vm.Lines);
        Assert.True(vm.Draft.IsEmpty);
    }
}
