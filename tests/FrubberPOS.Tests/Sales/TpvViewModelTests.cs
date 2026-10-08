using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Sales;

public sealed class TpvViewModelTests
{
    private static (TpvViewModel vm, FakeNavigator nav, FakeTicketPrinter printer) Build(ProductTestBed bed)
    {
        var nav = new FakeNavigator { NextQuantityMilli = 1_000 };
        var printer = new FakeTicketPrinter();
        return (new TpvViewModel(
            new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock),
            new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession()),
            new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units),
            printer,
            nav), nav, printer);
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
    public async Task EmptySearch_ListsAllActive_AndPreselectsFirst()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 4_500, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);

        var (vm, _, _) = Build(bed);

        // JHO-60: sin escribir nada, el TPV muestra todo el catálogo activo.
        Assert.Equal(string.Empty, vm.SearchText);
        await vm.SearchAsync();

        Assert.Equal(2, vm.Results.Count);
        Assert.NotNull(vm.SelectedResult); // listo para Enter
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

        var (vm, _, _) = Build(bed);
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

        var (vm, nav, _) = Build(bed);
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

        var (vm, nav, _) = Build(bed);
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

        var (vm, _, _) = Build(bed);
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

        var (vm, nav, _) = Build(bed);
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

        var (vm, _, _) = Build(bed);
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
        var (vm, nav, _) = Build(bed);

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

        var (vm, nav, _) = Build(bed);
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

        var (vm, _, _) = Build(bed);
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
        var (vm, _, _) = Build(bed);
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

        var (vm, _, _) = Build(bed);
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

        var (vm, _, _) = Build(bed);
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

        var (vm, _, _) = Build(bed);
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

        var (vm, nav, _) = Build(bed);
        nav.NextQuantityMilli = 1_250;
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        var line = Assert.Single(vm.Lines);
        Assert.Equal(1, line.Number);
        Assert.Equal("1,25 KG", line.QuantityText); // §8.6 cantidad con unidad (ceros finales recortados, coma es-CO)
        Assert.Equal("$ 6.000", line.PriceText);
        Assert.Equal("$ 7.500", line.TotalText);
    }

    [Fact]
    public async Task Line_DisplaysDecimalCommaPerKgPriceAndTotal()
    {
        // JHO-67 N4: "1,35 kg × $ 12.000/kg = $ 16.200".
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("4011", "Aguacate", Domain.Products.ProductUnit.Kg, 12_000, 0), CancellationToken.None);

        var (vm, nav, _) = Build(bed);
        nav.NextQuantityMilli = 1_350;
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        var line = Assert.Single(vm.Lines);
        Assert.Equal("1,35 KG", line.QuantityText);
        Assert.Equal("$ 12.000", line.PriceText);
        Assert.Equal(" /KG", line.UnitPriceSuffix);
        Assert.Equal("$ 16.200", line.TotalText);
    }

    [Fact]
    public async Task SearchResult_FixedThresholdMarksLowAndAgotado()
    {
        // JHO-67: umbral fijo aprobado por el dueño (sin columna Min en la DB):
        // 0 < stock ≤ 5 KG/LB/UND ⇒ "bajo"; stock = 0 ⇒ "agotado".
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("B1", "Bajon", Domain.Products.ProductUnit.Kg, 3_000, 5_000), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("A1", "Uvas", Domain.Products.ProductUnit.Kg, 3_000, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("S1", "Sandia", Domain.Products.ProductUnit.Kg, 3_000, 5_001), CancellationToken.None);

        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();

        var low = vm.Results.Single(r => r.Plu == "B1");
        Assert.True(low.IsLowStock);
        Assert.False(low.IsOutOfStock);
        Assert.Equal("bajo", low.StockStatusText);

        var agotado = vm.Results.Single(r => r.Plu == "A1");
        Assert.True(agotado.IsOutOfStock);
        Assert.False(agotado.IsLowStock);
        Assert.Equal("agotado", agotado.StockStatusText);

        var sano = vm.Results.Single(r => r.Plu == "S1");
        Assert.False(sano.IsLowStock);
        Assert.False(sano.IsOutOfStock);
        Assert.Equal(string.Empty, sano.StockStatusText);
    }

    [Fact]
    public async Task SearchResult_FixedThresholdAppliesToUnitsToo()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("U5", "Huevos", Domain.Products.ProductUnit.Unit, 400, 5_000), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("U6", "Gaseosa", Domain.Products.ProductUnit.Unit, 2_500, 6_000), CancellationToken.None);

        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();

        Assert.True(vm.Results.Single(r => r.Plu == "U5").IsLowStock);
        Assert.False(vm.Results.Single(r => r.Plu == "U6").IsLowStock);
    }

    [Fact]
    public async Task Toast_OnAdd_ShowsProductName_AndHidesAfterDuration()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        Assert.True(vm.ToastVisible); // M4 pastilla flotante sin foco
        Assert.Equal("Leche agregado", vm.ToastText);
        Assert.Equal(TimeSpan.FromMilliseconds(2400), TpvViewModel.ToastDuration);

        await Task.Delay(TpvViewModel.ToastDuration + TimeSpan.FromMilliseconds(400));

        Assert.False(vm.ToastVisible); // se sola oculta tras el tiempo del prototipo
    }

    [Fact]
    public async Task Toast_OnRemoveLine_UsesProductName()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();
        vm.SelectedLine = vm.Lines[0];

        await vm.RemoveSelectedLine();

        Assert.True(vm.ToastVisible);
        Assert.Equal("Leche eliminado", vm.ToastText); // el nombre se toma antes de borrar la línea
    }

    [Fact]
    public async Task Toast_OnClearDraft_ShowsVentaLimpiada()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        await vm.ClearDraftWithProtectionAsync(); // NextConfirmResult = true por defecto

        Assert.True(vm.Draft.IsEmpty);
        Assert.True(vm.ToastVisible);
        Assert.Equal("Venta limpiada", vm.ToastText);
    }

    [Fact]
    public async Task Toast_OnEditQuantity_ShowsCantidadActualizada()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 6_000, 0), CancellationToken.None);

        var (vm, nav, _) = Build(bed);
        nav.NextQuantityMilli = 1_000;
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();
        vm.SelectedLine = vm.Lines[0];
        nav.NextQuantityMilli = 2_500;

        await vm.EditQuantityAsync();

        Assert.True(vm.ToastVisible);
        Assert.Equal("Cantidad actualizada", vm.ToastText);
    }

    [Fact]
    public async Task Counters_MatchResultsAndLineState()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 4_500, 0), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, _, _) = Build(bed);

        Assert.Equal("0 productos", vm.ResultsCountText); // contadores M6
        Assert.True(vm.ResultsEmptyVisible);
        Assert.False(vm.ResultsTableVisible);
        Assert.Equal("0 ítems", vm.LineCountText);
        Assert.True(vm.TicketEmptyVisible);
        Assert.False(vm.TicketTableVisible);

        await vm.SearchAsync();

        Assert.Equal("2 productos", vm.ResultsCountText);
        Assert.False(vm.ResultsEmptyVisible);
        Assert.True(vm.ResultsTableVisible);

        vm.SelectedResult = vm.Results.Single(r => r.Plu == "L1");
        await vm.AddSelectedAsync();

        Assert.Equal("1 ítem", vm.LineCountText); // singular proto
        Assert.False(vm.TicketEmptyVisible);
        Assert.True(vm.TicketTableVisible);
    }

    [Fact]
    public async Task CanCharge_RequiresOpenCash_AndAtLeastOneLine()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);
        await SeedAdminUserAsync(bed);

        var (vm, _, _) = Build(bed);
        await vm.RefreshCashStateAsync(); // todavía no hay caja
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        Assert.False(vm.CanCharge); // con línea pero caja cerrada

        await new OpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock)
            .ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None);
        await vm.RefreshCashStateAsync();

        Assert.True(vm.CanCharge); // caja abierta + venta con productos
    }

    [Fact]
    public async Task OpenCashCommand_RaisesOpenCashRequested()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (vm, _, _) = Build(bed);
        var fired = 0;
        vm.OpenCashRequested += (_, _) => fired++;

        vm.OpenCashCommand.Execute(null);

        Assert.Equal(1, fired); // M5 el banner lleva directo a Caja
    }

    [Fact]
    public async Task Discount_ApplyAndRemove_UpdateChipPercentAndToasts()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var (vm, nav, _) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        Assert.False(vm.DiscountAppliedVisible);
        Assert.Equal("−$ 0", vm.DiscountDisplayText);
        Assert.False(vm.RemoveDiscountCommand.CanExecute(null));

        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 0, 0, 500);
        await vm.OpenDiscountAsync();

        Assert.True(vm.DiscountAppliedVisible); // chip quitar visible
        Assert.Equal("11%", vm.DiscountPercentText); // 500 de 4.200 = 11%
        Assert.Equal("−$ 500 (11%)", vm.DiscountDisplayText); // línea proto M6
        Assert.Equal("$ 3.700", vm.TotalText);
        Assert.Equal("Descuento del 11%", vm.ToastText);
        Assert.True(vm.RemoveDiscountCommand.CanExecute(null));

        vm.RemoveDiscountCommand.Execute(null);

        Assert.False(vm.DiscountAppliedVisible);
        Assert.Equal("−$ 0", vm.DiscountDisplayText);
        Assert.Equal("0%", vm.DiscountPercentText);
        Assert.Equal("$ 4.200", vm.TotalText);
        Assert.Equal("Descuento retirado", vm.ToastText);
        Assert.False(vm.RemoveDiscountCommand.CanExecute(null));
    }

    [Fact]
    public async Task SearchAndLines_ExposeAvatarAndUnitSuffix()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("M1", "Mango", Domain.Products.ProductUnit.Kg, 6_000, 0), CancellationToken.None);

        var (vm, nav, _) = Build(bed);
        nav.NextQuantityMilli = 1_000;
        await vm.SearchAsync();

        Assert.Equal("M", vm.Results.Single().FirstLetter); // avatar de la tabla menta

        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        Assert.Equal(" /KG", vm.Lines[0].UnitPriceSuffix); // §8.6 sufijo de precio por kilo
    }
}
