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
    public async Task SearchText_Typing_FiltersInRealTimeWithoutExplicitSearch()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("1001", "Manzana Gala", ProductUnit.Kg, 6000, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("2030", "Leche entera", ProductUnit.Unit, 4200, 0), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.SearchAsync();
        Assert.Equal(2, vm.Products.Count);

        // JHO-88: SIN llamar a SearchAsync a mano — el setter debe disparar
        // la búsqueda solo; se espera con plazo (patrón de polling del fix
        // de tests flaky) y se verifica que la tabla se filtra sola.
        vm.SearchText = "manzana";
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.Products.Count != 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, vm.Products.Count);
        Assert.Equal("1001", vm.Products[0].Plu);

        vm.SearchText = "leche";
        deadline = DateTime.UtcNow.AddSeconds(5);
        while ((vm.Products.Count != 1 || vm.Products[0].Plu != "2030") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal("2030", vm.Products[0].Plu);

        vm.SearchText = string.Empty;
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.Products.Count != 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2, vm.Products.Count);
    }

    [Fact]
    public async Task EmptyState_SearchWithoutMatch_ShowsMessageAndClearsSearch()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("1001", "Manzana Gala", ProductUnit.Kg, 6000, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("2030", "Leche entera", ProductUnit.Unit, 4200, 0), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.SearchAsync();
        Assert.Equal(2, vm.Products.Count);

        // JHO-89: buscar algo inexistente muestra el estado vacío (no tabla en blanco).
        vm.SearchText = "zzz";
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!vm.EmptyVisible && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(vm.EmptyVisible);
        Assert.False(vm.RowsVisible);
        Assert.Equal(0, vm.Products.Count);
        Assert.Equal("No encontramos productos", vm.EmptyTitle);
        Assert.Equal("Ningún producto coincide con \"zzz\". Prueba con otro nombre o PLU.", vm.EmptySub);
        Assert.Equal("Limpiar búsqueda", vm.EmptyCtaText);

        // El CTA borra la búsqueda y restaura la lista en tiempo real.
        vm.EmptyCtaCommand!.Execute(null);
        deadline = DateTime.UtcNow.AddSeconds(5);
        while ((vm.Products.Count != 2 || vm.EmptyVisible) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2, vm.Products.Count);
        Assert.False(vm.EmptyVisible);
        Assert.True(vm.RowsVisible);
    }

    [Fact]
    public async Task EmptyState_EmptyCatalog_InvitesCreatingProduct()
    {
        await using var bed = await ProductTestBed.CreateAsync();

        var (vm, _) = Build(bed);
        await vm.SearchAsync();

        Assert.True(vm.EmptyVisible);
        Assert.False(vm.RowsVisible);
        Assert.Equal("No hay productos para mostrar", vm.EmptyTitle);
        Assert.Equal("Empieza creando tu primer producto para tu catálogo", vm.EmptySub);
        Assert.Equal("Crear producto", vm.EmptyCtaText);
        Assert.Same(vm.NewCommand, vm.EmptyCtaCommand);
    }

    [Fact]
    public async Task EmptyState_NoInactive_SaysAllActive()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("1001", "Manzana Gala", ProductUnit.Kg, 6000, 0), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.SetFilterAsync(ProductStatusFilter.Inactivos);

        Assert.True(vm.EmptyVisible);
        Assert.Equal("No hay productos para mostrar", vm.EmptyTitle);
        Assert.Equal("Todos tus productos están activos. Buen trabajo.", vm.EmptySub);
        Assert.Equal("Crear producto", vm.EmptyCtaText);
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
