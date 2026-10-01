using Dapper;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;
using System.Windows.Media;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// History page ViewModel (JHO-46, P-UX §13.1/13.2/13.5): date
/// validation, paging availability, row formatting with text+color state.
/// </summary>
public sealed class SalesHistoryViewModelTests
{
    private static async Task<ProductTestBed> BedWithSalesAsync(int count)
    {
        var bed = await ProductTestBed.CreateAsync();
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@UserId, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { UserId = 1, Salt = new byte[16], Hash = new byte[32] });
        await connection.ExecuteAsync(
            "INSERT INTO CashSession (CashSessionId, OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1, 1, 0, 1, 'Admin', 0);");

        var local = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Unspecified);
        var baseMs = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUnixTimeMilliseconds();
        for (long number = 1; number <= count; number++)
        {
            await connection.ExecuteAsync(
                "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
                + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
                + " VALUES (@Number, 1, 1, 'Admin', @Moment, @Offset, 1000, 0, 1000, 'CASH', 1000, 0);",
                new
                {
                    Number = number,
                    Moment = baseMs + number * 1_000,
                    Offset = (int)TimeZoneInfo.Local.GetUtcOffset(local).TotalMinutes
                });
        }

        return bed;
    }

    private static (SalesHistoryViewModel vm, FakeNavigator nav) Build(ProductTestBed bed)
    {
        var nav = new FakeNavigator();
        return (new SalesHistoryViewModel(
            new SearchSalesHistoryUseCase(bed.History, bed.Units, ProductTestBed.AdminSession()), nav), nav);
    }

    [Fact]
    public async Task InvalidDates_ShowMessageAndDoNotSearch()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (vm, _) = Build(bed);
        vm.FromText = "no-es-fecha";

        await vm.SearchAsync();

        Assert.Contains("dd/MM/aaaa", vm.ErrorMessage);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task EndBeforeStart_ShowsRuleMessage()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (vm, _) = Build(bed);
        vm.FromText = "30/09/2026";
        vm.ToText = "29/09/2026";

        await vm.SearchAsync();

        Assert.Equal("Hasta no puede ser anterior a Desde.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Paging_FirstPageBlocksPreviousAndEnablesNext()
    {
        await using var bed = await BedWithSalesAsync(120);
        var (vm, _) = Build(bed);
        vm.FromText = "30/09/2026";
        vm.ToText = "30/09/2026";

        await vm.SearchAsync();

        Assert.Equal(1, vm.Page);
        Assert.Equal(50, vm.Items.Count);
        Assert.Equal(120, vm.TotalCount);
        Assert.False(vm.PreviousPageCommand.CanExecute(null));
        Assert.True(vm.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task MovingToNextPage_LoadsTheNextFifty()
    {
        await using var bed = await BedWithSalesAsync(120);
        var (vm, _) = Build(bed);
        vm.FromText = "30/09/2026";
        vm.ToText = "30/09/2026";

        await vm.SearchAsync(2);

        Assert.Equal(2, vm.Page);
        Assert.Equal(50, vm.Items.Count);
        Assert.True(vm.PreviousPageCommand.CanExecute(null));
        Assert.True(vm.NextPageCommand.CanExecute(null));
        Assert.Equal("V-000070", vm.Items[0].NumberText); // más reciente de la página 2
    }

    [Fact]
    public async Task LastPage_DisablesNext()
    {
        await using var bed = await BedWithSalesAsync(120);
        var (vm, _) = Build(bed);
        vm.FromText = "30/09/2026";
        vm.ToText = "30/09/2026";

        await vm.SearchAsync(3);

        Assert.Equal(3, vm.Page);
        Assert.Equal(20, vm.Items.Count);
        Assert.True(vm.PreviousPageCommand.CanExecute(null));
        Assert.False(vm.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task Rows_FormatNumberMomentPaymentTotalAndStatusWithColor()
    {
        await using var bed = await BedWithSalesAsync(2);
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
                + " VALUES (1, 1800000000000, -300, 1, 'Admin');");
        }

        var (vm, _) = Build(bed);
        vm.FromText = "30/09/2026";
        vm.ToText = "30/09/2026";
        await vm.SearchAsync();

        Assert.Equal(2, vm.Items.Count);
        var newest = vm.Items[0]; // más reciente primero
        Assert.Equal("V-000002", newest.NumberText);
        Assert.Equal("Efectivo", newest.PaymentText);
        Assert.Equal("$ 1.000", newest.TotalText);
        Assert.Equal("Vigente", newest.StatusText);
        Assert.Matches(@"\d{2}/\d{2}/\d{4} \d{2}:\d{2}", newest.MomentText);

        var voided = vm.Items[1];
        Assert.Equal("Anulada", voided.StatusText);
        Assert.NotEqual(
            ((SolidColorBrush)newest.StatusBrush).Color,
            ((SolidColorBrush)voided.StatusBrush).Color);
    }

    [Fact]
    public async Task OpenDetail_WhenVoidedInside_RefreshesTheList()
    {
        await using var bed = await BedWithSalesAsync(1);
        var (vm, nav) = Build(bed);
        vm.FromText = "30/09/2026";
        vm.ToText = "30/09/2026";
        await vm.SearchAsync();
        Assert.Equal("Vigente", vm.Items[0].StatusText);

        // La anulación real ocurre dentro del detalle (simulada): refresco esperado.
        nav.NextSaleDetailVoided = true;
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
                + " VALUES (1, 1800000000000, -300, 1, 'Admin');");
        }

        await vm.OpenDetailAsync();

        Assert.Equal(1, nav.ShowSaleDetailCount);
        Assert.Equal(1L, nav.LastSaleDetailNumber);
        Assert.Equal("Anulada", vm.Items[0].StatusText);
    }
}
