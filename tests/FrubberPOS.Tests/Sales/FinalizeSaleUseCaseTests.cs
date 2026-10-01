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
/// Finalization rules (JHO-43, RN-CAJ-002, RN-VEN-001/002): open session
/// required at confirm, revalidation, snapshots persisted, inventory and
/// numbering updated exactly once; failure rolls everything back.
/// </summary>
public sealed class FinalizeSaleUseCaseTests
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

    private static async Task<int> CountSalesAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM Sale;");
    }

    private static async Task<long> GetStockAsync(string path, int productId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<long>("SELECT StockMilli FROM Product WHERE ProductId = @Id;", new { Id = productId });
    }

    private static FinalizeSaleUseCase UseCase(ProductTestBed bed) =>
        new(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock);

    [Fact]
    public async Task Finalize_HappyPath_PersistsSnapshotsAndStock()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var created = await create.ExecuteAsync(
            new CreateProductRequest("L1", "Leche entera", ProductUnit.Unit, 4_200, 5_000), CancellationToken.None);
        await OpenCashAsync(bed);

        var lines = new DraftSaleLine[]
        {
            new(created.Value.ProductId, "L1", "Leche entera", ProductUnit.Unit, 4_200, 1_000)
        };
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest(lines, 0, "CASH", 5_000), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SaleNumber);
        Assert.Equal(4_200, result.Value.TotalCop);
        Assert.Equal(4_000, await GetStockAsync(bed.DbPath, created.Value.ProductId)); // vendió una unidad

        var summary = new GetCashSessionSummaryUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession());
        var sessionId = 1;
        var s = (await summary.ExecuteAsync(new GetCashSessionSummaryRequest(sessionId), CancellationToken.None)).Value.Summary;
        Assert.Equal(4_200, s.CashSalesCop);
        Assert.Equal(4_200, s.NetSalesCop);
        Assert.Equal(1, s.SaleCount);
    }

    [Fact]
    public async Task Finalize_NoOpenCash_ConflictAndNothingPersisted()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var created = await create.ExecuteAsync(
            new CreateProductRequest("L1", "Leche entera", ProductUnit.Unit, 4_200, 0), CancellationToken.None);

        var lines = new DraftSaleLine[]
        {
            new(created.Value.ProductId, "L1", "Leche entera", ProductUnit.Unit, 4_200, 1_000)
        };
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest(lines, 0, "CASH", 5_000), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(0, await CountSalesAsync(bed.DbPath));
    }

    [Fact]
    public async Task Finalize_PriceChanged_RollsBack()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var created = await create.ExecuteAsync(
            new CreateProductRequest("L1", "Leche entera", ProductUnit.Unit, 4_200, 2_000), CancellationToken.None);
        await OpenCashAsync(bed);

        var lines = new DraftSaleLine[]
        {
            new(created.Value.ProductId, "L1", "Leche entera", ProductUnit.Unit, 9_999, 1_000) // precio viejo en el draft
        };
        var result = await UseCase(bed).ExecuteAsync(
            new FinalizeSaleRequest(lines, 0, "CASH", 10_000), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(0, await CountSalesAsync(bed.DbPath));
        Assert.Equal(2_000, await GetStockAsync(bed.DbPath, created.Value.ProductId)); // sin consumir numeración ni stock
    }
}
