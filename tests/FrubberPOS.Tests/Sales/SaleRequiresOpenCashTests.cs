using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// RN-CAJ-002 / RF-CAJ-013/014: una venta no puede finalizarse sin una caja
/// abierta. El rechazo no debe persistir venta, líneas, movimiento de
/// inventario ni consumir el consecutivo; el TPV debe avisar al operador
/// (P-UX-010) y no abrir el cobro.
/// </summary>
public sealed class SaleRequiresOpenCashTests
{
    private const string Plu = "L1";
    private const string ProductName = "Leche entera";
    private const long PriceCop = 4_200;
    private const long InitialStockMilli = 5_000;

    [Fact]
    public async Task Finalize_WithoutOpenCash_IsRejectedAndLeavesNoTrace()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await CreateProductAsync(bed);

        // No se abre ninguna caja: la venta no puede completarse (RN-CAJ-002).
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(productId)], 0, "CASH", 10_000), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Contains("No hay caja abierta", result.Error.Message);
        Assert.Contains("Abra caja", result.Error.Message);

        // Nada quedó escrito ni consumido.
        Assert.Equal(0, await CountAsync("Sale"));
        Assert.Equal(0, await CountAsync("SaleLine"));
        Assert.Equal(0, await CountAsync("SaleVoid"));
        Assert.Equal(0, await CountAsync("CashSession"));
        Assert.Equal(InitialStockMilli, await GetStockAsync(productId));
        Assert.Equal(0, await GetSaleSequenceAsync());
    }

    [Fact]
    public async Task Finalize_AfterCashWasClosed_IsRejectedAndLeavesNoTrace()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await CreateProductAsync(bed);

        // La caja existió, pero se cerró: tampoco hay sesión abierta.
        await OpenCashAsync(bed);
        var close = new CloseCashSessionUseCase(
            bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate);
        var closed = await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None);
        Assert.True(closed.IsSuccess, closed.Error?.Message);

        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(productId)], 0, "CASH", 10_000), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Contains("No hay caja abierta", result.Error.Message);

        Assert.Equal(0, await CountAsync("Sale"));
        Assert.Equal(0, await CountAsync("SaleLine"));
        Assert.Equal(0, await CountAsync("SaleVoid"));
        Assert.Equal(1, await CountAsync("CashSession"));
        Assert.Equal(1, await CountAsync("CashSession", "ClosedAtUtcMs IS NOT NULL"));
        Assert.Equal(InitialStockMilli, await GetStockAsync(productId));
        Assert.Equal(0, await GetSaleSequenceAsync());
    }

    [Fact]
    public async Task Tpv_WithoutOpenCash_WarnsAndBlocksChargeWithoutPersisting()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await CreateProductAsync(bed);
        var (vm, nav, _) = Build(bed);

        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        // P-UX-010: aviso persistente y cobro deshabilitado.
        Assert.False(vm.IsCashOpen);
        Assert.True(vm.CashWarningVisible);
        Assert.Equal(TpvViewModel.NoCashWarningText, vm.CashWarningText);
        Assert.Contains("Sin caja abierta", vm.CashWarningText);
        Assert.Contains("F10", vm.CashWarningText);
        Assert.False(vm.CanCharge);
        Assert.False(vm.OpenCheckoutCommand.CanExecute(null));

        // Aun llamando al cobro directamente (sin pasar por el comando), se
        // informa y no se abre el diálogo ni se registra nada.
        await vm.OpenCheckoutAsync();

        Assert.Equal(0, nav.ShowCheckoutCount);
        Assert.Equal(TpvViewModel.NoCashWarningText, vm.ErrorMessage);
        Assert.Single(vm.Lines);
        Assert.Equal(0, await CountAsync("Sale"));
        Assert.Equal(0, await CountAsync("CashSession"));
        Assert.Equal(InitialStockMilli, await GetStockAsync(productId));
        Assert.Equal(0, await GetSaleSequenceAsync());
    }

    [Fact]
    public async Task Tpv_WhenCashOpens_WarningClearsAndChargeUnlocks()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await CreateProductAsync(bed);
        var (vm, _, _) = Build(bed);

        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();
        Assert.False(vm.IsCashOpen);

        await OpenCashAsync(bed);
        await vm.RefreshCashStateAsync();

        // El aviso sigue el estado real de la caja, no es un texto fijo.
        Assert.True(vm.IsCashOpen);
        Assert.False(vm.CashWarningVisible);
        Assert.Equal(string.Empty, vm.CashWarningText);
        Assert.True(vm.CanCharge);
        Assert.True(vm.OpenCheckoutCommand.CanExecute(null));
    }

    private static FinalizeSaleUseCase UseCase(ProductTestBed bed) =>
        new(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate);

    private static DraftSaleLine Line(int productId) =>
        new(productId, Plu, ProductName, ProductUnit.Unit, PriceCop, 1_000);

    private static async Task<int> CreateProductAsync(ProductTestBed bed)
    {
        var created = await new CreateProductUseCase(
                bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(
                new CreateProductRequest(Plu, ProductName, ProductUnit.Unit, PriceCop, InitialStockMilli),
                CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        return created.Value.ProductId;
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

    private static async Task OpenCashAsync(ProductTestBed bed)
    {
        await using (var connection = PostgresTestDb.NewConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
                + " VALUES (1, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
                new { Salt = new byte[16], Hash = new byte[32] });
        }

        var open = new OpenCashSessionUseCase(
            bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate);
        var result = await open.ExecuteAsync(new OpenCashSessionRequest(50_000), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private static async Task<int> CountAsync(string table, string? where = null)
    {
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        var sql = $"SELECT COUNT(*) FROM {table}";
        if (where is not null)
        {
            sql += " WHERE " + where;
        }

        return await connection.ExecuteScalarAsync<int>(sql + ";");
    }

    private static async Task<long> GetStockAsync(int productId)
    {
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT StockMilli FROM Product WHERE ProductId = @ProductId;", new { ProductId = productId });
    }

    private static async Task<long> GetSaleSequenceAsync()
    {
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT LastValue FROM SystemSequence WHERE SequenceName = 'SaleNumber';");
    }
}
