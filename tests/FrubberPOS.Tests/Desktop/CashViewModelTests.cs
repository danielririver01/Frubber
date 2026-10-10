using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;
using FrubberPOS.Infrastructure.Persistence.Products;
using FrubberPOS.Infrastructure.Persistence.Sales;
using FrubberPOS.Tests.Cash;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

public sealed class CashViewModelTests
{
    private static (CashViewModel vm, FakeNavigator nav) Build(CashTestBed bed)
    {
        var nav = new FakeNavigator();
        var session = CashTestBed.AdminSession();
        var vm = new CashViewModel(
            new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession()),
            new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock),
            new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock),
            new CorrectOpeningBalanceUseCase(bed.Sessions, bed.Units, session),
            session,
            nav);
        return (vm, nav);
    }

    [Fact]
    public async Task Refresh_NoOpenSession_ShowsSinCajaState()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var (vm, _) = Build(bed);

        await vm.RefreshAsync();

        Assert.False(vm.IsOpen);
        Assert.Equal("Sin caja", vm.StateText);
    }

    [Fact]
    public async Task Open_ValidBalance_ShowsOpenStateAndTotals()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var (vm, _) = Build(bed);
        await vm.RefreshAsync();

        vm.OpeningBalanceText = "150.000";
        await vm.OpenAsync();

        Assert.True(vm.IsOpen);
        Assert.Equal("Caja abierta", vm.StateText);
        Assert.Equal("Admin", vm.OpenedByText);
        Assert.Equal("$ 150.000", vm.SessionOpeningBalanceDisplay);
        Assert.Equal("$ 150.000", vm.ExpectedCashText);
        Assert.Equal("0 ventas", vm.SaleCountText);
    }

    [Fact]
    public async Task Open_InvalidBalance_ShowsInlineError()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var (vm, _) = Build(bed);

        vm.OpeningBalanceText = "abc";
        await vm.OpenAsync();

        Assert.False(vm.IsOpen);
        Assert.Equal("Escriba el saldo inicial en pesos enteros.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Close_WithoutConfirm_DoesNothing()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        vm.OpeningBalanceText = "0";
        await vm.OpenAsync();
        nav.NextConfirmResult = false;

        await vm.CloseAsync();

        Assert.True(vm.IsOpen);
    }

    [Fact]
    public async Task Close_WithConfirm_ReturnsToSinCaja()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        vm.OpeningBalanceText = "0";
        await vm.OpenAsync();

        await vm.CloseAsync();

        Assert.False(vm.IsOpen);
        Assert.Equal("Sin caja", vm.StateText);
    }

    [Fact]
    public async Task Open_WhenAlreadyOpen_ShowsConflict()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var (vm, _) = Build(bed);
        vm.OpeningBalanceText = "0";
        await vm.OpenAsync();

        // Segundo VM intenta abrir de nuevo → Conflicto
        var (vm2, _) = Build(bed);
        await vm2.OpenAsync();

        Assert.False(vm2.IsOpen);
        Assert.Contains("Ya hay una caja abierta", vm2.ErrorMessage);
    }

    [Fact]
    public async Task CorrectBalance_WithSales_ShowsError_AndDoesNotOpenDialog()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        vm.OpeningBalanceText = "10.000";
        await vm.OpenAsync();

        var created = await new CreateProductUseCase(new DapperProductRepository(), bed.Units, CashTestBed.AdminSession())
            .ExecuteAsync(new CreateProductRequest("M1", "Mango", ProductUnit.Kg, 6_000, 5_000), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var finalized = await new FinalizeSaleUseCase(
                new DapperSaleRepository(), bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock)
            .ExecuteAsync(
                new FinalizeSaleRequest([new DraftSaleLine(created.Value.ProductId, "M1", "Mango", ProductUnit.Kg, 6_000, 1_000)], 0, "CASH", 10_000),
                CancellationToken.None);
        Assert.True(finalized.IsSuccess, finalized.Error?.Message);

        await vm.RefreshAsync(); // la vista recarga al volver (JHO-82)

        // JHO-82: con ventas el clic sigue respondiendo (el comando se ejecuta).
        Assert.True(vm.CanCorrectBalance);
        Assert.True(vm.CorrectBalanceCommand.CanExecute(null));

        await vm.CorrectBalanceAsync();

        Assert.Contains("ya tiene ventas", vm.ErrorMessage);
        Assert.Equal(0, nav.AskOpeningBalanceCorrectionCount);
    }

    [Fact]
    public async Task CorrectBalance_WithoutSales_Confirms_UpdatesBalance()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        vm.OpeningBalanceText = "10.000";
        await vm.OpenAsync();
        nav.NextOpeningBalanceCorrection = 50_000;

        await vm.CorrectBalanceAsync();

        Assert.Equal(1, nav.AskOpeningBalanceCorrectionCount);
        Assert.Equal("$ 50.000", vm.SessionOpeningBalanceDisplay);
        Assert.Contains("corregido", vm.StatusMessage);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    [Fact]
    public async Task CorrectBalance_Cancelled_LeavesBalanceUntouched()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var (vm, nav) = Build(bed);
        vm.OpeningBalanceText = "10.000";
        await vm.OpenAsync();
        nav.NextOpeningBalanceCorrection = null;

        await vm.CorrectBalanceAsync();

        Assert.Equal(1, nav.AskOpeningBalanceCorrectionCount);
        Assert.Equal("$ 10.000", vm.SessionOpeningBalanceDisplay);
        Assert.Equal(string.Empty, vm.ErrorMessage);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }
}
