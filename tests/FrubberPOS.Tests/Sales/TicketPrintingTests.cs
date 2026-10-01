using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// Optional ticket printing after a committed sale (JHO-45, RN-IMP-001/005):
/// nothing prints unless the operator asks, a canceled picker prints
/// nothing, and a printer failure never re-runs the cobro — the message
/// states the sale stayed saved (P-UX §9.6).
/// </summary>
public sealed class TicketPrintingTests
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

    private static async Task<int> CountSalesAsync(ProductTestBed bed)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sale;");
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

    private static async Task<ProductTestBed> BedWithLecheAsync()
    {
        var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);
        await OpenCashAsync(bed);
        return bed;
    }

    [Fact]
    public async Task SaleSaved_ShowsConfirmationWithNumberTotalPaymentAndChange()
    {
        await using var bed = await BedWithLecheAsync();
        var nav = new FakeNavigator { NextQuantityMilli = 1_000 };
        var printer = new FakeTicketPrinter();
        var vm = new TpvViewModel(
            new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock),
            new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession()),
            new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units),
            printer,
            nav);
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();
        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 5_000, 800, 0);
        await vm.OpenCheckoutAsync();

        Assert.Equal(1, nav.ShowSaleSavedCount);
        Assert.NotNull(nav.LastSaleSaved);
        Assert.Equal(1, nav.LastSaleSaved!.SaleNumber);
        Assert.Equal(4_200, nav.LastSaleSaved.TotalCop);
        Assert.Equal("CASH", nav.LastSaleSaved.PaymentMethod);
        Assert.Equal(5_000, nav.LastSaleSaved.ReceivedCop);
        Assert.Equal(800, nav.LastSaleSaved.ChangeCop);
    }

    [Fact]
    public async Task NoPrintRequested_NothingIsAskedOrPrinted()
    {
        await using var bed = await BedWithLecheAsync();
        var (vm, nav, printer) = Build(bed);
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = new CheckoutResultDto(true, "TRANSFER", 0, 0, 0);
        nav.NextSaleSavedPrintRequested = false;
        await vm.OpenCheckoutAsync();

        Assert.NotNull(vm.LastSaleNumber);
        Assert.Equal(0, nav.AskPrinterCount);
        Assert.Equal(0, printer.PrintCount);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    [Fact]
    public async Task PrintRequested_PrintsTheHistoricalTicketOnce()
    {
        await using var bed = await BedWithLecheAsync();
        var (vm, nav, printer) = Build(bed);
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = new CheckoutResultDto(true, "TRANSFER", 0, 0, 0);
        nav.NextSaleSavedPrintRequested = true;
        nav.NextPrinterName = "HP LaserJet";
        await vm.OpenCheckoutAsync();

        Assert.Equal(1, nav.AskPrinterCount);
        Assert.Equal(1, printer.PrintCount);
        Assert.Equal("HP LaserJet", printer.LastPrinterName);
        Assert.NotNull(printer.LastTicket);
        Assert.Equal("V-000001", printer.LastTicket!.SaleNumberText);
        Assert.Contains(printer.LastTicket.Lines, line => line.Contains("Leche"));
        Assert.Contains(printer.LastTicket.Lines, line => line.Contains("TOTAL") && line.Contains("$ 4.200"));
        Assert.Equal(1, await CountSalesAsync(bed));
    }

    [Fact]
    public async Task PickerCanceled_PrintsNothingAndKeepsSilence()
    {
        await using var bed = await BedWithLecheAsync();
        var (vm, nav, printer) = Build(bed);
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = new CheckoutResultDto(true, "TRANSFER", 0, 0, 0);
        nav.NextSaleSavedPrintRequested = true;
        nav.NextPrinterName = null; // "ninguna" / cancel
        await vm.OpenCheckoutAsync();

        Assert.Equal(1, nav.AskPrinterCount);
        Assert.Equal(0, printer.PrintCount);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    [Fact]
    public async Task PrintFailure_SaleStaysSavedAndMessageSaysSo()
    {
        await using var bed = await BedWithLecheAsync();
        var (vm, nav, printer) = Build(bed);
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = new CheckoutResultDto(true, "TRANSFER", 0, 0, 0);
        nav.NextSaleSavedPrintRequested = true;
        nav.NextPrinterName = "HP LaserJet";
        printer.NextError = "La impresora seleccionada no está disponible.";
        await vm.OpenCheckoutAsync();

        Assert.Equal(1, printer.PrintCount);
        Assert.Equal(
            "La venta V-000001 quedó guardada, pero el ticket no pudo imprimirse.",
            vm.ErrorMessage);
        Assert.Equal(1, await CountSalesAsync(bed));
        Assert.True(vm.Draft.IsEmpty);
        Assert.Equal(1, vm.LastSaleNumber);
    }
}
