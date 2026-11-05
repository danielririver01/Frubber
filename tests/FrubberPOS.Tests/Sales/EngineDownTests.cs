using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// Engine-down behaviour (JHO-113 · ADR-029, criterio 3): when the
/// PostgreSQL server cannot be reached, the caja is informed
/// «Sin conexión con el servidor» and no sale is registered — never an
/// unhandled crash. The dead endpoint is a local port with no listener,
/// so the connection attempt fails immediately with connection refused.
/// </summary>
public sealed class EngineDownTests
{
    // No listener on this port: connect fails fast (connection refused).
    private const string DeadConnectionString =
        "Host=localhost;Port=59999;Database=frubberpos_test;Username=frubber;Password=frubber;Pooling=false;";

    private static NpgsqlUnitOfWorkFactory DeadUnits() =>
        new(new NpgsqlConnectionFactory(new DatabaseOptions(DeadConnectionString)));

    private static void AssertConnectionFailure(Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Connection, result.Error!.Code);
        Assert.Equal(DatabaseUnavailableException.UserMessage, result.Error.Message);
    }

    private static async Task<int> CountSalesAsync()
    {
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sale;");
    }

    [Fact]
    public async Task FinalizeSale_EngineDown_InformsAndRegistersNothing()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var finalize = new FinalizeSaleUseCase(
            bed.Sales, bed.Sessions, DeadUnits(), ProductTestBed.AdminSession(), bed.Clock, bed.Gate);

        var result = await finalize.ExecuteAsync(
            new FinalizeSaleRequest(
                [new DraftSaleLine(1, "1", "Mango", ProductUnit.Kg, 2_000, 1_000)],
                0, "CASH", 5_000),
            CancellationToken.None);

        AssertConnectionFailure(result);
        Assert.Equal(0, await CountSalesAsync());
    }

    [Fact]
    public async Task Search_EngineDown_InformsTheCashier()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var search = new SearchProductsForSaleUseCase(bed.Products, DeadUnits(), ProductTestBed.AdminSession());

        var result = await search.ExecuteAsync(new SearchForSaleRequest(string.Empty), CancellationToken.None);

        AssertConnectionFailure(result);
    }

    [Fact]
    public async Task GetOpenCash_EngineDown_ReportsConnectionFailure()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var getOpenCash = new GetOpenCashSessionUseCase(bed.Sessions, DeadUnits(), ProductTestBed.AdminSession());

        var result = await getOpenCash.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None);

        AssertConnectionFailure(result);
    }

    [Fact]
    public async Task Tpv_FinalizeWithEngineDown_InformsAndKeepsTheDraftUnregistered()
    {
        // Live search and cash state, dead finalize: the cashier builds the
        // sale normally, confirms the cobro and gets the connection notice.
        await using var bed = await BedWithProductAsync();
        var nav = new FakeNavigator { NextQuantityMilli = 1_000 };
        var vm = new TpvViewModel(
            new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new FinalizeSaleUseCase(bed.Sales, bed.Sessions, DeadUnits(), ProductTestBed.AdminSession(), bed.Clock, bed.Gate),
            new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession()),
            new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units),
            new FakeTicketPrinter(),
            nav);

        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        nav.NextCheckout = new CheckoutResultDto(true, "CASH", 5_000, 800, 0);
        await vm.OpenCheckoutAsync();

        Assert.Equal(DatabaseUnavailableException.UserMessage, vm.ErrorMessage);
        Assert.Equal(0, await CountSalesAsync());
        Assert.False(vm.Draft.IsEmpty); // la venta sigue en preparación: no se registró
    }

    [Fact]
    public async Task Tpv_EngineDownOnCashState_InformsAndBlocksTheCheckout()
    {
        // The cash state cannot be read: the caja informs and the Cobrar
        // command stays disabled even with a full draft.
        await using var bed = await BedWithProductAsync();
        var nav = new FakeNavigator { NextQuantityMilli = 1_000 };
        var vm = new TpvViewModel(
            new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate),
            new GetOpenCashSessionUseCase(bed.Sessions, DeadUnits(), ProductTestBed.AdminSession()),
            new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units),
            new FakeTicketPrinter(),
            nav);

        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();
        await vm.RefreshCashStateAsync();

        Assert.Equal(DatabaseUnavailableException.UserMessage, vm.ErrorMessage);
        Assert.False(vm.IsCashOpen);
        Assert.False(vm.OpenCheckoutCommand.CanExecute(null));
        Assert.Equal(0, await CountSalesAsync());
    }

    private static async Task<ProductTestBed> BedWithProductAsync()
    {
        var bed = await ProductTestBed.CreateAsync();
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest("L1", "Leche", ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);
        await OpenCashAsync(bed);
        return bed;
    }

    private static async Task OpenCashAsync(ProductTestBed bed)
    {
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (1, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { Salt = new byte[16], Hash = new byte[32] });
        await connection.ExecuteAsync(
            "INSERT INTO CashSession (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1, 0, 1, 'Admin', 0);");
    }
}
