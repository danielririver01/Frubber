using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;
using Npgsql;

using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Cash;
using FrubberPOS.Infrastructure.Persistence.Sales;
namespace FrubberPOS.Tests.Sales;

/// <summary>
/// Sale finalization (JHO-44, RN-VEN-001…016): stock, sequencing,
/// validation and payment methods against a real PostgreSQL database.
/// </summary>
public sealed class FinalizeSaleUseCaseTests
{
    private static FinalizeSaleUseCase UseCase(ProductTestBed bed) =>
        new(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate);

    private static async Task OpenCashAsync(ProductTestBed bed)
    {
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@UserId, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { UserId = 1, Salt = new byte[16], Hash = new byte[32] });
        await connection.ExecuteAsync(
            "INSERT INTO CashSession (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1, 0, 1, 'Admin', 0);");
    }

    [Fact]
    public async Task Finalize_TransferWithReceived_Validation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await SellableProductAsync(bed);
        await OpenCashAsync(bed);

        // RN-VEN-012: transferencia no solicita ni acepta recibido/cambio.
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(created)], 0, "TRANSFER", 5_000), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Contains("Transferencia", result.Error.Message);
    }

    [Fact]
    public async Task Finalize_NegativeDiscount_Validation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await SellableProductAsync(bed);
        await OpenCashAsync(bed);

        // RN-VEN-008: el descuento es mayor o igual a cero, también en el caso de uso.
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(created)], -500, "CASH", 50_000), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal("DiscountCop", result.Error.Field);
        Assert.Equal(0, await CountSalesAsync(bed.Units));
    }

    [Fact]
    public async Task Finalize_ZeroQuantityLine_Validation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await SellableProductAsync(bed);
        await OpenCashAsync(bed);

        // RN-UNI-004: toda cantidad agregada a una venta debe ser mayor que cero.
        var line = new DraftSaleLine(created, "L1", "Leche entera", ProductUnit.Unit, 4_200, 0);
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([line], 0, "CASH", 5_000), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Contains("no tiene cantidad válida", result.Error.Message);
    }

    [Fact]
    public async Task Finalize_FractionalUnitLine_Validation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await SellableProductAsync(bed);
        await OpenCashAsync(bed);

        // RN-UNI-003: los productos por unidad solo admiten cantidades enteras.
        var line = new DraftSaleLine(created, "L1", "Leche entera", ProductUnit.Unit, 4_200, 1_500);
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([line], 0, "CASH", 10_000), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Contains("debe ser entera", result.Error.Message);
    }

    [Fact]
    public async Task Finalize_InsufficientStock_CompletesSaleWithNegativeStock()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var created = await create.ExecuteAsync(
            new CreateProductRequest("S1", "Sin existencias", ProductUnit.Unit, 4_200, 0), CancellationToken.None);
        await OpenCashAsync(bed);

        // RN-INV-002 / PR-004: la venta continúa aunque no haya existencia; el resultado puede ser negativo.
        var lines = new DraftSaleLine[]
        {
            new(created.Value.ProductId, "S1", "Sin existencias", ProductUnit.Unit, 4_200, 2_000)
        };
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest(lines, 0, "CASH", 10_000), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(-2_000, await GetStockAsync(bed.Units, created.Value.ProductId));
        Assert.Equal(1, await CountSalesAsync(bed.Units));
    }

    [Fact]
    public async Task Finalize_ProductDeactivatedAfterDraft_ConflictAndNothingPersisted()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await SellableProductAsync(bed);
        await OpenCashAsync(bed);

        // El producto se desactivó después de agregarlo al borrador (RN-PRO-006 / RN-VEN-002).
        var deactivate = new SetProductActiveStateUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        Assert.True((await deactivate.ExecuteAsync(
            new SetProductActiveStateRequest(created, false), CancellationToken.None)).IsSuccess);

        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(created)], 0, "CASH", 5_000), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Contains("está inactivo", result.Error.Message);
        Assert.Equal(0, await CountSalesAsync(bed.Units));
    }

    [Fact]
    public async Task Finalize_TwoSales_NumbersAreSequential()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await SellableProductAsync(bed);
        await OpenCashAsync(bed);

        // RN-VEN-015: consecutivo global, único y creciente.
        var first = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(created)], 0, "CASH", 5_000), CancellationToken.None);
        var second = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(created)], 0, "CASH", 5_000), CancellationToken.None);

        Assert.True(first.IsSuccess && second.IsSuccess);
        Assert.Equal(1, first.Value.SaleNumber);
        Assert.Equal(2, second.Value.SaleNumber);
    }

    [Fact]
    public async Task Finalize_AfterReopen_ContinuesTheSequence()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await SellableProductAsync(bed);
        await OpenCashAsync(bed);
        Assert.True((await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(created)], 0, "CASH", 5_000), CancellationToken.None)).IsSuccess);

        // RN-VEN-015: el consecutivo es persistente; un "reinicio" (fábrica y
        // repositorios nuevos sobre la misma base) continúa donde quedó.
        var units = PostgresTestDb.NewUnitFactory();
        var sales = new DapperSaleRepository();
        var sessions = new DapperCashSessionRepository();
        var useCase = new FinalizeSaleUseCase(
            sales, sessions, units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate);

        var result = await useCase.ExecuteAsync(
            new FinalizeSaleRequest([Line(created)], 0, "CASH", 5_000), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.SaleNumber);
    }

    [Fact]
    public async Task Finalize_AsCashier_Succeeds()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var created = await create.ExecuteAsync(
            new CreateProductRequest("L1", "Leche entera", ProductUnit.Unit, 4_200, 5_000), CancellationToken.None);
        await OpenCashAsync(bed);
        await using (var connection = PostgresTestDb.NewConnection())
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
                + " VALUES (2, 'Cajero', 'CASHIER', 1, @Salt, @Hash, 1, 1);",
                new { Salt = new byte[16], Hash = new byte[32] });
        }
        // RN-USR-004/RN-CAJ-003: el cajero vende y su identidad queda en la venta (RN-VEN-016).
        var finalize = new FinalizeSaleUseCase(
            bed.Sales, bed.Sessions, bed.Units, ProductTestBed.CashierSession(), bed.Clock, bed.Gate);
        var result = await finalize.ExecuteAsync(
            new FinalizeSaleRequest([Line(created.Value.ProductId)], 0, "CASH", 5_000), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        await using var verifyConnection = PostgresTestDb.NewConnection();
        await verifyConnection.OpenAsync();
        var cashierId = await verifyConnection.ExecuteScalarAsync<int>(
            "SELECT CashierUserId FROM Sale WHERE SaleNumber = 1;");
        Assert.Equal(2, cashierId);
    }

    [Fact]
    public async Task Finalize_WhenGateBlocks_ReturnsLicenseBlocked()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var created = await SellableProductAsync(bed);
        await OpenCashAsync(bed);

        // P-DD §11.3 / RN-LIC-002: prueba vencida o reloj retrocedido bloquea la venta.
        bed.Gate.Blocked = new Error(ErrorCode.LicenseBlocked, "El periodo de prueba terminó. Active la licencia para continuar.");
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest([Line(created)], 0, "CASH", 5_000), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.LicenseBlocked, result.Error!.Code);
        Assert.Equal(0, await CountSalesAsync(bed.Units));
    }

    private static async Task<int> SellableProductAsync(ProductTestBed bed)
    {
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var created = await create.ExecuteAsync(
            new CreateProductRequest("L1", "Leche entera", ProductUnit.Unit, 4_200, 5_000), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        return created.Value.ProductId;
    }

    private static DraftSaleLine Line(int productId) =>
        new(productId, "L1", "Leche entera", ProductUnit.Unit, 4_200, 1_000);

    private static async Task<long> GetStockAsync(NpgsqlUnitOfWorkFactory units, int productId)
    {
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT StockMilli FROM Product WHERE ProductId = @Id;", new { Id = productId });
    }

    private static async Task<int> CountSalesAsync(NpgsqlUnitOfWorkFactory units)
    {
        await using var connection = PostgresTestDb.NewConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sale;");
    }
}
