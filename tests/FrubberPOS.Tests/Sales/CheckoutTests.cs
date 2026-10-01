using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Sales;

public sealed class CheckoutViewModelTests
{
    [Fact]
    public void Cash_InsufficientReceived_BlocksConfirm()
    {
        var vm = new CheckoutViewModel();
        vm.Load(18_450, 0);
        vm.IsCash = true;
        vm.ReceivedText = "10.000";

        vm.Confirm();

        Assert.False(vm.Result.Confirmed);
        Assert.Contains("no alcanza", vm.ErrorMessage);
    }

    [Fact]
    public void Cash_SufficientReceived_ComputesChange()
    {
        var vm = new CheckoutViewModel();
        vm.Load(17_450, 1_000);
        vm.ReceivedText = "20.000";

        vm.Confirm();

        Assert.True(vm.Result.Confirmed);
        Assert.True(vm.Result.IsCash);
        Assert.Equal(20_000, vm.Result.ReceivedCop);
        Assert.Equal(3_550, vm.Result.ChangeCop); // 20.000 − (17.450 − 1.000) = 3.550
    }

    [Fact]
    public void Transfer_DisablesCashFields_AndIgnoresReceived()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);
        vm.IsCash = false;
        vm.ReceivedText = "0";

        vm.Confirm();

        Assert.True(vm.Result.Confirmed);
        Assert.False(vm.Result.IsCash);
        Assert.Equal(0, vm.Result.ChangeCop);
        Assert.False(vm.ShowCashFields);
    }

    [Fact]
    public void Discount_GreaterThanSubtotal_BlocksConfirm()
    {
        var vm = new CheckoutViewModel();
        vm.Load(5_000, 0);
        vm.DiscountText = "6.000";
        vm.ReceivedText = "0";

        vm.Confirm();

        Assert.False(vm.Result.Confirmed);
        Assert.Contains("descuento", vm.ErrorMessage);
    }

    [Fact]
    public void Cancel_LeavesUnconfirmedResult()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);

        vm.Cancel();

        Assert.False(vm.Result.Confirmed);
    }
}

public sealed class TpvViewModelCheckoutTests
{
    private static (TpvViewModel vm, FakeNavigator nav) Build(ProductTestBed bed)
    {
        var nav = new FakeNavigator { NextQuantityMilli = 1_000 };
        return (new TpvViewModel(
            new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()), nav), nav);
    }

    [Fact]
    public async Task OpenDiscount_Confirmed_AppliesDiscount()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);

        var (vm, nav) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 4_200, 3_200, 1_000);
        await vm.OpenDiscountAsync();

        Assert.Equal(1_000, vm.DiscountCop);
        Assert.Equal(3_200, vm.TotalCop);
        Assert.Equal("$ 3.200", vm.TotalText);
        Assert.Null(vm.LastConfirmedCheckout);
    }

    [Fact]
    public async Task OpenCheckout_Confirmed_SetsFinalCheckout()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);

        var (vm, nav) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = new CheckoutResultDto(true, "TRANSFER", 0, 0, 0);
        await vm.OpenCheckoutAsync();

        Assert.NotNull(vm.LastConfirmedCheckout);
        Assert.Equal("TRANSFER", vm.LastConfirmedCheckout.PaymentMethod);
    }

    [Fact]
    public async Task OpenCheckout_Canceled_DoesNotTouchDraft()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);

        var (vm, nav) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = null; // cancelado
        await vm.OpenCheckoutAsync();

        Assert.Null(vm.LastConfirmedCheckout);
        Assert.Single(vm.Lines);
    }
}
