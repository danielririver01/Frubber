using Dapper;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// Sale detail dialog ViewModel (JHO-46, P-UX §13.3/13.4): read-only
/// snapshot rendering, Anular only for ADMIN with an open cash session,
/// confirm-cancel leaves everything untouched.
/// </summary>
public sealed class SaleDetailViewModelTests
{
    private static (SaleDetailViewModel vm, FakeNavigator nav) Build(ProductTestBed bed)
    {
        var nav = new FakeNavigator();
        return (new SaleDetailViewModel(
            new GetSaleDetailUseCase(bed.History, bed.Units, ProductTestBed.AdminSession()),
            new VoidSaleUseCase(bed.Voids, bed.Sales, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate),
            nav), nav);
    }

    private static async Task<int> SellMangoAsync(ProductTestBed bed)
    {
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
                + " VALUES (@UserId, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
                new { UserId = 1, Salt = new byte[16], Hash = new byte[32] });
            await connection.ExecuteAsync(
                "INSERT INTO CashSession (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
                + " VALUES (1, 0, 1, 'Admin', 0);");
        }

        var created = await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new CreateProductRequest("M1", "Mango", ProductUnit.Kg, 6_000, 5_000), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);

        var finalized = await new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate)
            .ExecuteAsync(
                new FinalizeSaleRequest(
                    [new DraftSaleLine(created.Value.ProductId, "M1", "Mango", ProductUnit.Kg, 6_000, 1_250)],
                    500, "CASH", 8_000),
                CancellationToken.None);
        Assert.True(finalized.IsSuccess, finalized.Error?.Message);
        return created.Value.ProductId;
    }

    [Fact]
    public async Task Load_RendersHeaderLinesAndPayment()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SellMangoAsync(bed);
        var (vm, _) = Build(bed);

        await vm.LoadAsync(1, viewerIsAdmin: true);

        Assert.Equal("V-000001", vm.SaleNumberText);
        Assert.Equal("Caja #1", vm.CashSessionText);
        Assert.Equal("Cajero: Admin", vm.CashierText);
        Assert.Equal("Medio de pago: Efectivo", vm.PaymentText);
        Assert.Contains("$ 8.000", vm.CashDetailText);
        Assert.Contains("$ 1.000", vm.CashDetailText);
        Assert.Equal("$ 7.500", vm.SubtotalText);
        Assert.Equal("$ 500", vm.DiscountText);
        Assert.Equal("$ 7.000", vm.TotalText);
        Assert.Equal("Vigente", vm.StatusText);
        Assert.False(vm.VoidInfoVisible);

        var line = Assert.Single(vm.Lines);
        Assert.Equal("Mango", line.Name);
        Assert.Equal("1,25 KG", line.QuantityText);
        Assert.Equal("$ 7.500", line.TotalText);
    }

    [Fact]
    public async Task CashierViewer_NeverSeesTheVoidButton()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SellMangoAsync(bed);
        var (vm, _) = Build(bed);

        await vm.LoadAsync(1, viewerIsAdmin: false);

        Assert.False(vm.VoidButtonVisible);
        Assert.False(vm.CanVoid);
        Assert.False(vm.VoidCommand.CanExecute(null));
    }

    [Fact]
    public async Task Admin_ActiveSaleWithOpenCash_CanVoid_AndVoidRefreshesToAnulada()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SellMangoAsync(bed);
        var (vm, nav) = Build(bed);

        await vm.LoadAsync(1, viewerIsAdmin: true);
        Assert.True(vm.VoidButtonVisible);
        Assert.True(vm.CanVoid);

        nav.NextSaleVoidConfirmed = true;
        await vm.VoidAsync();

        Assert.True(vm.VoidedHere);
        Assert.True(vm.IsVoided);
        Assert.Equal("Anulada", vm.StatusText);
        Assert.True(vm.VoidInfoVisible);
        Assert.Contains("Anulada por Admin", vm.VoidInfoText);
        Assert.False(vm.CanVoid); // ya no se puede repetir desde el detalle
    }

    [Fact]
    public async Task VoidCanceled_LeavesTheSaleUntouched()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SellMangoAsync(bed);
        var (vm, nav) = Build(bed);
        await vm.LoadAsync(1, viewerIsAdmin: true);

        nav.NextSaleVoidConfirmed = false;
        await vm.VoidAsync();

        Assert.False(vm.VoidedHere);
        Assert.Equal("Vigente", vm.StatusText);
        Assert.Equal(1, nav.ConfirmSaleVoidCount);
        Assert.Equal(5_000 - 1_250, await StockAsync(bed, 1));
    }

    [Fact]
    public async Task ClosedCashSession_DisablesVoid()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SellMangoAsync(bed);
        var close = new FrubberPOS.Application.Cash.CloseCashSessionUseCase(
            bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate);
        Assert.True((await close.ExecuteAsync(new FrubberPOS.Application.Cash.CloseCashSessionRequest(), CancellationToken.None)).IsSuccess);

        var (vm, _) = Build(bed);
        await vm.LoadAsync(1, viewerIsAdmin: true);

        Assert.True(vm.VoidButtonVisible); // el rol sí lo ve
        Assert.False(vm.CanVoid);          // pero la regla lo deshabilita
        Assert.False(vm.VoidCommand.CanExecute(null));
    }

    private static async Task<long> StockAsync(ProductTestBed bed, int productId)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT StockMilli FROM Product WHERE ProductId = @ProductId;", new { ProductId = productId });
    }
}
