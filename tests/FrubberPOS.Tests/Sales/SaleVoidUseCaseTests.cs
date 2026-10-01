using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// Full transactional void (JHO-49, RN-HIS-005…010): admin only, active
/// sale with its cash session still open, one atomic write marks the sale
/// and reintegrates the stock; repeating the void is refused without a
/// second reintegration.
/// </summary>
public sealed class SaleVoidUseCaseTests
{
    private static VoidSaleUseCase Sut(ProductTestBed bed, FakeCurrentSession? session = null) =>
        new(bed.Voids, bed.Sales, bed.Units, session ?? ProductTestBed.AdminSession(), bed.Clock);

    /// <summary>Seeds user + open cash and sells 1.250 KG of Mango at $ 6.000 (stock 5.000 → 3.750).</summary>
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

        var finalized = await new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock)
            .ExecuteAsync(
                new FinalizeSaleRequest(
                    [new DraftSaleLine(created.Value.ProductId, "M1", "Mango", ProductUnit.Kg, 6_000, 1_250)],
                    0, "CASH", 8_000),
                CancellationToken.None);
        Assert.True(finalized.IsSuccess, finalized.Error?.Message);
        return created.Value.ProductId;
    }

    private static async Task<long> StockOfAsync(ProductTestBed bed, int productId)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT StockMilli FROM Product WHERE ProductId = @ProductId;", new { ProductId = productId });
    }

    [Fact]
    public async Task Void_MarksTheSaleAndReintegratesStock_Atomically()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await SellMangoAsync(bed);
        Assert.Equal(3_750, await StockOfAsync(bed, productId));

        var result = await Sut(bed).ExecuteAsync(new VoidSaleRequest(1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, result.Value.SaleNumber);

        // Inventario reintegrado (RN-HIS-008) y venta marcada, no eliminada (RN-HIS-009).
        Assert.Equal(5_000, await StockOfAsync(bed, productId));
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        var sale = await connection.QuerySingleAsync<SaleAfterVoidRow>(
            "SELECT SaleStatus AS Status, AdminNameSnapshot AS VoidedBy FROM V_SaleHistory WHERE SaleNumber = 1;");
        Assert.Equal("VOIDED", sale.Status);
        Assert.Equal("Admin", sale.VoidedBy); // trazabilidad automática (RN-HIS-010)
        var cashTotals = await connection.ExecuteScalarAsync<long>(
            "SELECT CashSalesCop FROM V_OpenCashTotals WHERE CashSessionId = 1;");
        Assert.Equal(0, cashTotals); // sale de los totales de la caja abierta
    }

    [Fact]
    public async Task RepeatVoid_IsRefused_AndNeverReintegratesTwice()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await SellMangoAsync(bed);
        Assert.True((await Sut(bed).ExecuteAsync(new VoidSaleRequest(1), CancellationToken.None)).IsSuccess);

        var second = await Sut(bed).ExecuteAsync(new VoidSaleRequest(1), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorCode.Conflict, second.Error!.Code);
        Assert.Contains("ya está anulada", second.Error.Message);
        Assert.Equal(5_000, await StockOfAsync(bed, productId));
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SaleVoid;"));
    }

    [Fact]
    public async Task ClosedSession_IsRefused()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await SellMangoAsync(bed);
        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock);
        var closed = await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None);
        Assert.True(closed.IsSuccess, closed.Error?.Message);

        var result = await Sut(bed).ExecuteAsync(new VoidSaleRequest(1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Contains("mientras su caja siga abierta", result.Error.Message);
        Assert.Equal(3_750, await StockOfAsync(bed, productId));
    }

    [Fact]
    public async Task Cashier_IsDenied()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SellMangoAsync(bed);

        var result = await Sut(bed, ProductTestBed.CashierSession()).ExecuteAsync(
            new VoidSaleRequest(1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }

    [Fact]
    public async Task UnknownSale_IsNotFound()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await Sut(bed).ExecuteAsync(new VoidSaleRequest(99), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
        Assert.Contains("V-000099", result.Error.Message);
    }

    [Fact]
    public async Task InvalidNumber_FailsValidation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await Sut(bed).ExecuteAsync(new VoidSaleRequest(0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
    }

    private sealed class SaleAfterVoidRow
    {
        public string Status { get; set; } = string.Empty;

        public string VoidedBy { get; set; } = string.Empty;
    }
}
