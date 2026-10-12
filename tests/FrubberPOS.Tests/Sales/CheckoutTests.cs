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

    [Fact]
    public void Confirm_RepeatedEnterAndLateCancel_NeverCloseTwice()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);
        vm.ReceivedText = "10.000";
        var closes = new List<bool>();
        vm.RequestClose += (_, confirmed) => closes.Add(confirmed);

        vm.Confirm();
        vm.Confirm(); // Enter/F9 repetido (B2)
        vm.Cancel();  // Esc tardío tras confirmar (B2)

        Assert.True(vm.IsConfirming);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.Equal(new[] { true }, closes); // RequestClose una sola vez (RN-VEN-002)
        Assert.True(vm.Result.Confirmed);
    }

    [Fact]
    public void Load_ResetsConfirming_SoTheDialogCanBeConfirmedAgain()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);
        vm.ReceivedText = "10.000";
        vm.Confirm();
        Assert.True(vm.IsConfirming);

        vm.Load(8_000, 0);
        vm.ReceivedText = "8.000";
        vm.Confirm();

        Assert.True(vm.IsConfirming);
        Assert.Equal(8_000, vm.Result.ReceivedCop);
    }

    [Fact]
    public void ChangeBox_CashEmpty_ShowsNeutralPrompt()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);

        Assert.Equal("neutral", vm.ChangeTone);
        Assert.Equal("Escribe el efectivo recibido", vm.ChangeLabelText);
        Assert.Equal(string.Empty, vm.ChangeValueText);
    }

    [Fact]
    public void ChangeBox_CashExact_ShowsPagoExacto()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);
        vm.ReceivedText = "10000";

        Assert.Equal("good", vm.ChangeTone);
        Assert.Equal("Pago exacto ✓", vm.ChangeLabelText);
        Assert.Equal("$ 0", vm.ChangeValueText);
    }

    [Fact]
    public void ChangeBox_CashOverTotal_ShowsCambioAEntregar()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);
        vm.ReceivedText = "15000";

        Assert.Equal("good", vm.ChangeTone);
        Assert.Equal("Cambio a entregar", vm.ChangeLabelText);
        Assert.Equal("$ 5.000", vm.ChangeValueText);
    }

    [Fact]
    public void ChangeBox_CashShort_ShowsFaltan()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);
        vm.ReceivedText = "6000";

        Assert.Equal("bad", vm.ChangeTone);
        Assert.Equal("Faltan", vm.ChangeLabelText);
        Assert.Equal("$ 4.000", vm.ChangeValueText);
    }

    [Fact]
    public void ChangeBox_Transfer_ShowsPagoExactoPorTransferencia()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);
        vm.IsCash = false;

        Assert.Equal("neutral", vm.ChangeTone);
        Assert.Equal("Pago exacto por transferencia", vm.ChangeLabelText);
        Assert.Equal("$ 10.000", vm.ChangeValueText);
        Assert.False(vm.ShowCashFields);
    }

    [Fact]
    public void ChangeBox_DiscountEdit_RefreshesTone()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);
        vm.ReceivedText = "6000";
        Assert.Equal("bad", vm.ChangeTone);

        vm.DiscountText = "4000"; // total = 6.000 = recibido

        Assert.Equal("good", vm.ChangeTone);
        Assert.Equal("Pago exacto ✓", vm.ChangeLabelText);
    }

    [Fact]
    public void QuickAmount_Exacto_UsesCurrentTotal()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 1_000);

        vm.ApplyQuickAmount("exacto");

        Assert.Equal("9000", vm.ReceivedText);
        Assert.Equal("good", vm.ChangeTone);
        Assert.Equal("Pago exacto ✓", vm.ChangeLabelText);
    }

    [Fact]
    public void QuickAmount_Number_SetsReceived()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);

        vm.ApplyQuickAmount("20000");

        Assert.Equal("20000", vm.ReceivedText);
        Assert.Equal("Cambio a entregar", vm.ChangeLabelText);
        Assert.Equal("$ 10.000", vm.ChangeValueText);
    }

    [Fact]
    public void RevealDiscountEditor_HiddenByDefault_ShownAfterLinkOrF6()
    {
        var vm = new CheckoutViewModel();
        vm.Load(10_000, 0);
        Assert.False(vm.IsDiscountEditorVisible);

        vm.RevealDiscountEditor();
        Assert.True(vm.IsDiscountEditorVisible);

        vm.Load(10_000, 0, revealDiscountEditor: true); // F6 abre revelado
        Assert.True(vm.IsDiscountEditorVisible);

        vm.Load(10_000, 0);
        Assert.False(vm.IsDiscountEditorVisible);
    }

    [Fact]
    public void ItemsSubtitle_CountAndDiscountDisplay_FollowProto()
    {
        var vm = new CheckoutViewModel();
        vm.Load(12_000, 0, itemCount: 1);

        Assert.Equal("1", vm.ItemCountText);
        Assert.Equal("1 ítem · $ 12.000", vm.ItemsSubtitleText);
        Assert.Equal("-$ 0", vm.DiscountDisplayText);

        vm.Load(20_000, 1_090, itemCount: 2);

        Assert.Equal("2", vm.ItemCountText);
        Assert.Equal("2 ítems · $ 18.910", vm.ItemsSubtitleText);
        Assert.Equal("-$ 1.090", vm.DiscountDisplayText);
    }
}

public sealed class TpvViewModelCheckoutTests
{
    private static async Task OpenCashAsync(ProductTestBed bed)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={bed.DbPath}");
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
            new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate),
            new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession()),
            new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units),
            printer,
            nav), nav, printer);
    }

    [Fact]
    public async Task OpenDiscount_Confirmed_AppliesDiscount()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);

        var (vm, nav, _) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 4_200, 3_200, 1_000);
        await vm.OpenDiscountAsync();

        Assert.Equal(1_000, vm.DiscountCop);
        Assert.Equal(3_200, vm.TotalCop);
        Assert.Equal("$ 3.200", vm.TotalText);
        Assert.Null(vm.LastConfirmedCheckout);
        Assert.True(nav.LastCheckoutDiscountMode); // F6 abre el modal con el editor (N2)
        Assert.Equal(1, nav.LastCheckoutItemCount);
    }

    [Fact]
    public async Task OpenCheckout_Confirmed_SetsFinalCheckout()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);

        var (vm, nav, _) = Build(bed);
        await OpenCashAsync(bed);
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = new CheckoutResultDto(true, "TRANSFER", 0, 0, 0);
        await vm.OpenCheckoutAsync();

        Assert.NotNull(vm.LastConfirmedCheckout);
        Assert.Equal("TRANSFER", vm.LastConfirmedCheckout!.PaymentMethod);
        Assert.False(nav.LastCheckoutDiscountMode); // F8 no revela el descuento
        Assert.Equal(1, nav.LastCheckoutItemCount);
    }

    [Fact]
    public async Task OpenCheckout_Canceled_DoesNotTouchDraft()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);

        var (vm, nav, _) = Build(bed);
        await OpenCashAsync(bed);
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = null; // cancelado
        await vm.OpenCheckoutAsync();

        Assert.Null(vm.LastConfirmedCheckout);
        Assert.Single(vm.Lines);
    }
}
