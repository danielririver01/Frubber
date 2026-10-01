using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// Critical sale-flow robustness (JHO-39): double submit never creates two
/// sales, cash/transfer/discount/change persist exactly, conflicts
/// revalidate the draft without corruption and a printer failure neither
/// reverts nor duplicates the sale.
/// </summary>
public sealed class SaleFlowRobustnessTests
{
    private static async Task OpenCashAsync(ProductTestBed bed)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@UserId, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { UserId = 1, Salt = new byte[16], Hash = new byte[32] });
        await connection.ExecuteAsync(
            "INSERT INTO CashSession (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1, 0, 1, 'Admin', 0);");
    }

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

    private static async Task<ProductTestBed> BedWithProductAsync(
        string plu, string name, ProductUnit unit, long priceCop, long stockMilli)
    {
        var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest(plu, name, unit, priceCop, stockMilli), CancellationToken.None);
        await OpenCashAsync(bed);
        return bed;
    }

    private static async Task AddFirstResultAsync(TpvViewModel vm)
    {
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();
    }

    private static async Task<int> CountSalesAsync(ProductTestBed bed)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sale;");
    }

    private static async Task<T> QuerySingleAsync<T>(ProductTestBed bed, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<T>(sql);
    }

    private sealed class SaleRow
    {
        public long TotalCop { get; set; }

        public long DiscountCop { get; set; }

        public string PaymentMethod { get; set; } = string.Empty;

        public long? ReceivedCop { get; set; }

        public long? ChangeCop { get; set; }
    }

    [Fact]
    public async Task ReentrantEnter_WhileDialogOpen_CreatesASingleSale()
    {
        await using var bed = await BedWithProductAsync("L1", "Leche", ProductUnit.Unit, 4_200, 24_000);
        var (vm, nav, _) = Build(bed);
        await AddFirstResultAsync(vm);

        // Segundo Enter mientras el diálogo sigue abierto (re-entrada).
        nav.OnShowCheckout = () => _ = vm.OpenCheckoutAsync();
        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 5_000, 800, 0);
        await vm.OpenCheckoutAsync();

        Assert.Equal(1, nav.ShowCheckoutCount);
        Assert.Equal(1, await CountSalesAsync(bed));
    }

    [Fact]
    public async Task DoubleClick_AfterFirstSale_FindsAnEmptyDraft()
    {
        await using var bed = await BedWithProductAsync("L1", "Leche", ProductUnit.Unit, 4_200, 24_000);
        var (vm, nav, _) = Build(bed);
        await AddFirstResultAsync(vm);

        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 5_000, 800, 0);
        await vm.OpenCheckoutAsync();
        await vm.OpenCheckoutAsync(); // doble clic tras cerrar el diálogo

        Assert.Equal(1, nav.ShowCheckoutCount);
        Assert.Equal(1, await CountSalesAsync(bed));
        Assert.True(vm.Draft.IsEmpty);
        Assert.Equal("No hay productos en la venta.", vm.ErrorMessage);
    }

    [Fact]
    public async Task CashSale_PersistsMethodReceivedAndChange()
    {
        await using var bed = await BedWithProductAsync("L1", "Leche", ProductUnit.Unit, 4_200, 24_000);
        var (vm, nav, _) = Build(bed);
        await AddFirstResultAsync(vm);

        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 5_000, 800, 0);
        await vm.OpenCheckoutAsync();

        var sale = await QuerySingleAsync<SaleRow>(bed, "SELECT TotalCop, DiscountCop, PaymentMethod, ReceivedCop, ChangeCop FROM Sale;");
        Assert.Equal("CASH", sale.PaymentMethod);
        Assert.Equal(4_200, sale.TotalCop);
        Assert.Equal(5_000, sale.ReceivedCop);
        Assert.Equal(800, sale.ChangeCop);
    }

    [Fact]
    public async Task TransferSale_PersistsWithoutReceivedOrChange()
    {
        await using var bed = await BedWithProductAsync("L1", "Leche", ProductUnit.Unit, 4_200, 24_000);
        var (vm, nav, _) = Build(bed);
        await AddFirstResultAsync(vm);

        nav.NextCheckout = new CheckoutResultDto(true, "TRANSFER", 0, 0, 0);
        await vm.OpenCheckoutAsync();

        var sale = await QuerySingleAsync<SaleRow>(bed, "SELECT TotalCop, DiscountCop, PaymentMethod, ReceivedCop, ChangeCop FROM Sale;");
        Assert.Equal("TRANSFER", sale.PaymentMethod);
        Assert.Equal(4_200, sale.TotalCop);
        Assert.Null(sale.ReceivedCop);
        Assert.Null(sale.ChangeCop);
    }

    [Fact]
    public async Task GlobalDiscount_PersistsDiscountAndReducedTotal()
    {
        await using var bed = await BedWithProductAsync("L1", "Leche", ProductUnit.Unit, 4_200, 24_000);
        var (vm, nav, _) = Build(bed);
        await AddFirstResultAsync(vm);

        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 4_000, 800, 1_000);
        await vm.OpenCheckoutAsync();

        var sale = await QuerySingleAsync<SaleRow>(bed, "SELECT TotalCop, DiscountCop, PaymentMethod, ReceivedCop, ChangeCop FROM Sale;");
        Assert.Equal(1_000, sale.DiscountCop);
        Assert.Equal(3_200, sale.TotalCop);
        Assert.Equal(800, sale.ChangeCop); // 4.000 − 3.200
    }

    [Fact]
    public async Task FullDiscount_IsRejected_AndNothingPersisted()
    {
        await using var bed = await BedWithProductAsync("L1", "Leche", ProductUnit.Unit, 4_200, 24_000);
        var (vm, nav, _) = Build(bed);
        await AddFirstResultAsync(vm);

        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 5_000, 800, 4_200);
        await vm.OpenCheckoutAsync();

        Assert.Contains("El total no es válido", vm.ErrorMessage);
        Assert.Equal(0, await CountSalesAsync(bed));
        Assert.Single(vm.Lines); // el borrador sobrevive al rechazo
    }

    [Theory]
    [InlineData(1_999, 500, 1_000)]  // 999,5 → 1.000 (mitad hacia arriba)
    [InlineData(2_001, 500, 1_001)]  // 1.000,5 → 1.001
    [InlineData(3_333, 500, 1_667)]  // 1.666,5 → 1.667
    [InlineData(3_333, 501, 1_670)]  // 1.669,833 → 1.670
    public void LineTotal_RoundsHalfUp_InWholeCop(long priceCop, long quantityMilli, long expectedCop)
    {
        var draft = new DraftSale();
        var product = new ProductDto(1, "P1", "Fresa", ProductUnit.Kg, priceCop, 0, true, true);

        Assert.True(draft.AddOrAccumulate(product, quantityMilli).IsSuccess);
        Assert.Equal(expectedCop, draft.Lines.Single().LineTotalCop);
    }

    [Fact]
    public void Accumulation_LbQuantities_MergeIntoOneLine()
    {
        var draft = new DraftSale();
        var product = new ProductDto(1, "Q1", "Queso", ProductUnit.Lb, 12_000, 0, true, true);

        Assert.True(draft.AddOrAccumulate(product, 500).IsSuccess);
        Assert.True(draft.AddOrAccumulate(product, 250).IsSuccess);

        var line = Assert.Single(draft.Lines);
        Assert.Equal(750, line.QuantityMilli);
        Assert.Equal(9_000, line.LineTotalCop); // 0,750 lb × $ 12.000
    }

    [Fact]
    public async Task PriceConflict_RevalidatesDraftWithoutCorruption()
    {
        await using var bed = await BedWithProductAsync("L1", "Leche", ProductUnit.Unit, 4_200, 24_000);
        var (vm, nav, _) = Build(bed);
        await AddFirstResultAsync(vm);

        // El precio cambia en el catálogo entre el agregado y el cobro.
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("UPDATE Product SET PriceCop = 5_000 WHERE Plu = 'L1';");
        }

        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 5_000, 800, 0);
        await vm.OpenCheckoutAsync();

        // Nada se persistió y el borrador sigue íntegro.
        Assert.Contains("cambió", vm.ErrorMessage);
        Assert.Equal(0, await CountSalesAsync(bed));
        var line = Assert.Single(vm.Lines);
        Assert.Equal("Leche", line.Name);
        Assert.Equal(4_200, line.Line.UnitPriceCop);
        Assert.Equal(1_000, line.QuantityMilli);

        // Ni el stock ni la caja se tocaron.
        var stock = await QuerySingleAsync<long>(bed, "SELECT StockMilli FROM Product WHERE Plu = 'L1';");
        Assert.Equal(24_000, stock);
        var openSessions = await QuerySingleAsync<int>(bed, "SELECT COUNT(*) FROM CashSession WHERE ClosedAtUtcMs IS NULL;");
        Assert.Equal(1, openSessions);
    }

    [Fact]
    public async Task PrinterFailure_LeavesNoSequenceGapNorDuplicate()
    {
        await using var bed = await BedWithProductAsync("L1", "Leche", ProductUnit.Unit, 4_200, 24_000);
        var (vm, nav, printer) = Build(bed);
        await AddFirstResultAsync(vm);

        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 5_000, 800, 0);
        nav.NextSaleSavedPrintRequested = true;
        nav.NextPrinterName = "POS-58";
        printer.NextError = "La impresora seleccionada no está disponible.";
        await vm.OpenCheckoutAsync();

        Assert.Equal(1, await CountSalesAsync(bed));
        // La secuencia solo consumió un número: no hay huecos ni duplicados.
        var lastNumber = await QuerySingleAsync<long>(bed, "SELECT LastValue FROM SystemSequence WHERE SequenceName = 'SaleNumber';");
        Assert.Equal(1, lastNumber);
        Assert.Equal(
            "La venta V-000001 quedó guardada, pero el ticket no pudo imprimirse.",
            vm.ErrorMessage);
    }
}
