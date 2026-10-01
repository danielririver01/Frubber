using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Sales;

public sealed class TpvViewModelTests
{
    private static (TpvViewModel vm, FakeNavigator nav) Build(ProductTestBed bed)
    {
        var nav = new FakeNavigator { NextQuantityMilli = 1_000 };
        return (new TpvViewModel(
            new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()), nav), nav);
    }

    [Fact]
    public async Task Search_PopulatesResultsWithActiveOnly()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 4_500, 0), CancellationToken.None);
        var inactive = await create.ExecuteAsync(new CreateProductRequest("M2", "Mango Criollo", Domain.Products.ProductUnit.Kg, 4_000, 0), CancellationToken.None);
        await new SetProductActiveStateUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new SetProductActiveStateRequest(inactive.Value.ProductId, false), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.SearchAsync();

        Assert.Single(vm.Results);
        Assert.Equal("M1", vm.Results[0].Plu);
    }

    [Fact]
    public async Task AddSelected_UnitProduct_AddsOneUnitWithoutDialog()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, nav) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();

        await vm.AddSelectedAsync();

        Assert.Equal(0, nav.AskQuantityCount); // UNIT entra directo (P-UX-009)
        Assert.Single(vm.Lines);
        Assert.Equal(1_000, vm.Lines[0].QuantityMilli);
        Assert.Equal("$ 4.200", vm.SubtotalText);
    }

    [Fact]
    public async Task AddSelected_KgProduct_OpensQuantityDialog()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 6_000, 0), CancellationToken.None);

        var (vm, nav) = Build(bed);
        nav.NextQuantityMilli = 1_250;
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();

        await vm.AddSelectedAsync();

        Assert.Equal(1, nav.AskQuantityCount);
        var line = Assert.Single(vm.Lines);
        Assert.Equal(1_250, line.QuantityMilli);
        Assert.Equal(7_500, line.LineTotalCop);
    }

    [Fact]
    public async Task AddSameProductTwice_AccumulatesOneLine()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();

        await vm.AddSelectedAsync();
        await vm.AddSelectedAsync();

        Assert.Single(vm.Lines);
        Assert.Equal(2_000, vm.Lines[0].QuantityMilli);
    }

    [Fact]
    public async Task EditQuantity_SetsNewValue()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 6_000, 0), CancellationToken.None);

        var (vm, nav) = Build(bed);
        nav.NextQuantityMilli = 1_000;
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextQuantityMilli = 2_500;
        vm.SelectedLine = vm.Lines[0];
        await vm.EditQuantityAsync();

        Assert.Equal(2_500, vm.Lines[0].QuantityMilli);
        Assert.Equal(15_000, vm.Lines[0].LineTotalCop);
    }

    [Fact]
    public async Task RemoveLine_DropsFromDraft()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        vm.SelectedLine = vm.Lines[0];
        await vm.RemoveSelectedLine();

        Assert.True(vm.Draft.IsEmpty);
        Assert.Empty(vm.Lines);
    }

    [Fact]
    public async Task ClearDraft_Empty_ActsDirectlyWithoutConfirm()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (vm, nav) = Build(bed);

        await vm.ClearDraftWithProtectionAsync();

        Assert.Equal(0, nav.ConfirmDiscardDraftCount);
        Assert.True(vm.Draft.IsEmpty);
    }

    [Fact]
    public async Task ClearDraft_WithLines_ConfirmsFirst()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, nav) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextConfirmResult = false; // Esc / Cancelar
        await vm.ClearDraftWithProtectionAsync();

        Assert.Equal(1, nav.ConfirmDiscardDraftCount);
        Assert.False(vm.Draft.IsEmpty); // Esc nunca descarta

        nav.NextConfirmResult = true; // Enter / F9
        await vm.ClearDraftWithProtectionAsync();

        Assert.Equal(2, nav.ConfirmDiscardDraftCount);
        Assert.True(vm.Draft.IsEmpty);
    }

    [Fact]
    public async Task Search_ReplacedQuery_NewResultsWin()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 4_500, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, _) = Build(bed);
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
        var (vm, _) = Build(bed);
        var fired = 0;
        vm.FocusSearchRequested += (_, _) => fired++;

        vm.RequestFocusSearch();

        Assert.Equal(1, fired);
        await vm.SearchAsync();
    }
}
