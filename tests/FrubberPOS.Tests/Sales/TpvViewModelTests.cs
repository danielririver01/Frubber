using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Sales;

public sealed class TpvViewModelTests
{
    private static (TpvViewModel vm, FakeNavigator nav) Build(ProductTestBed bed)
    {
        var nav = new FakeNavigator { NextQuantityMilli = 1_000 };
        return (new TpvViewModel(
            new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock),
            new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession()),
            nav), nav);
    }

    private static async Task SeedAdminUserAsync(ProductTestBed bed)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (1, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { Salt = new byte[16], Hash = new byte[32] });
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

    [Fact]
    public async Task RefreshCash_NoOpenSession_ShowsWarningAndBlocksOnlyCheckout()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        Assert.False(vm.IsCashOpen);
        Assert.True(vm.CashWarningVisible);
        Assert.Contains("Sin caja abierta", vm.CashWarningText);
        Assert.False(vm.OpenCheckoutCommand.CanExecute(null));
        Assert.Single(vm.Lines); // preparar no se bloquea (P-UX-010)
        Assert.True(vm.OpenDiscountCommand.CanExecute(null)); // descuento libre
    }

    [Fact]
    public async Task RefreshCash_OpenSession_HidesWarningAndEnablesCheckout()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);
        await SeedAdminUserAsync(bed);
        await new OpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock)
            .ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        Assert.True(vm.IsCashOpen);
        Assert.False(vm.CashWarningVisible);
        Assert.Equal(string.Empty, vm.CashWarningText);
        Assert.True(vm.OpenCheckoutCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddSameProductTwice_SelectsAccumulatedLine()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, _) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();

        await vm.AddSelectedAsync();
        await vm.AddSelectedAsync();

        Assert.NotNull(vm.SelectedLine); // §8.5 re-selecciona la línea acumulada
        Assert.Equal(1, vm.SelectedLine!.Number);
        Assert.Equal(2_000, vm.SelectedLine.QuantityMilli);
    }

    [Fact]
    public async Task Lines_ExposeFormattedColumns()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 6_000, 0), CancellationToken.None);

        var (vm, nav) = Build(bed);
        nav.NextQuantityMilli = 1_250;
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        var line = Assert.Single(vm.Lines);
        Assert.Equal(1, line.Number);
        Assert.Equal("1.25 KG", line.QuantityText); // §8.6 cantidad con unidad (ceros finales recortados)
        Assert.Equal("$ 6.000", line.PriceText);
        Assert.Equal("$ 7.500", line.TotalText);
    }
}
