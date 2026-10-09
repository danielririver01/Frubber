using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

public sealed class ProductsViewModelTests
{
    private static (ProductsViewModel vm, FakeNavigator nav) Build(ProductTestBed bed)
    {
        var nav = new FakeNavigator();
        var vm = new ProductsViewModel(
            new SearchProductsUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new SetProductActiveStateUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new DeleteProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            nav);
        return (vm, nav);
    }

    [Fact]
    public async Task Search_DefaultTodos_ListsBothAndChipsSwitch()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var mango = await create.ExecuteAsync(new CreateProductRequest("M1", "Mango", ProductUnit.Kg, 4500, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("B1", "Banano", ProductUnit.Unit, 500, 0), CancellationToken.None);
        await new SetProductActiveStateUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new SetProductActiveStateRequest(mango.Value.ProductId, false), CancellationToken.None);

        var (vm, _) = Build(bed);
        Assert.True(vm.IsFilterTodos);
        await vm.SearchAsync();

        Assert.Equal(2, vm.Products.Count);

        await vm.SetFilterAsync(ProductStatusFilter.Activos);
        Assert.True(vm.IsFilterActivos);
        Assert.Single(vm.Products);
        Assert.Equal("B1", vm.Products[0].Plu);
        Assert.Equal("Activo", vm.Products[0].StateText);
        Assert.Equal("$ 500", vm.Products[0].PriceText);

        await vm.SetFilterAsync(ProductStatusFilter.Inactivos);
        Assert.Single(vm.Products);
        Assert.Equal("M1", vm.Products[0].Plu);
        Assert.Equal("Inactivo", vm.Products[0].StateText);
    }

    [Fact]
    public async Task Search_ByText_FiltersRows()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("1001", "Manzana Gala", ProductUnit.Kg, 6000, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("2030", "Leche entera", ProductUnit.Unit, 4200, 0), CancellationToken.None);

        var (vm, _) = Build(bed);
        vm.SearchText = "manzana";
        await vm.SearchAsync();

        Assert.Single(vm.Products);
        Assert.Equal("1001", vm.Products[0].Plu);
    }

    [Fact]
    public async Task NegativeStock_RowShowsSignAndAccessibleText()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var admin = ProductTestBed.AdminSession();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, admin).ExecuteAsync(
            new CreateProductRequest("T1", "Tomate chonto", ProductUnit.Kg, 3900, 0), CancellationToken.None);
        await new AdjustStockUseCase(bed.Products, bed.Units, admin).ExecuteAsync(
            new AdjustStockRequest(created.Value.ProductId, -1250), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.SearchAsync();

        var row = Assert.Single(vm.Products);
        Assert.True(row.IsNegativeStock);
        Assert.Equal("-1,25 KG", row.StockText);
        Assert.StartsWith("Existencia negativa", row.StockA11yText);
    }

    [Fact]
    public async Task ToggleActive_RefreshesList()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Lechuga", ProductUnit.Kg, 5000, 0), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.SetFilterAsync(ProductStatusFilter.Activos);
        await vm.SearchAsync();
        vm.SelectedProduct = Assert.Single(vm.Products);

        await vm.ToggleActiveAsync();

        Assert.Empty(vm.Products); // activos filter: la desactivada sale de la lista
    }

    [Fact]
    public async Task Delete_WithoutConfirm_DoesNothing()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("D1", "Durazno", ProductUnit.Kg, 5500, 0), CancellationToken.None);

        var (vm, nav) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedProduct = Assert.Single(vm.Products);
        nav.NextConfirmResult = false;

        await vm.DeleteAsync();

        Assert.Single(vm.Products);
    }

    [Fact]
    public async Task Delete_WithHistory_ButtonDisabled_NoDeleteCall()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("H1", "Habanero", ProductUnit.Unit, 1000, 0), CancellationToken.None);
        await bed.SeedSaleLineAsync(created.Value.ProductId, "H1", "Habanero");

        var (vm, nav) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedProduct = Assert.Single(vm.Products);

        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task New_SavedRefresh_AddsRow()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        await vm.SearchAsync();
        Assert.Empty(vm.Products);

        // El diálogo real crea el producto; el fake solo reporta "guardado".
        nav.NextEditorResult = true;
        var createAfter = Task.Run(async () =>
        {
            await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
                new CreateProductRequest("N1", "Naranja", ProductUnit.Kg, 3000, 0), CancellationToken.None);
        });
        await createAfter;
        await vm.NewAsync();

        Assert.Equal(1, nav.ProductEditorCount);
        Assert.Single(vm.Products);
    }
}
