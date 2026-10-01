using Dapper;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// Historical sale detail (JHO-48, RN-HIS-001/002/003/004): admin-only,
/// snapshots only, voided sales show the administrator and moment, and
/// catalog edits never rewrite history.
/// </summary>
public sealed class GetSaleDetailUseCaseTests
{
    private static GetSaleDetailUseCase Sut(ProductTestBed bed, FakeCurrentSession? session = null) =>
        new(bed.History, bed.Units, session ?? ProductTestBed.AdminSession());

    /// <summary>Sells 1.250 KG of Mango at $ 6.000 = $ 7.500 in cash and returns the product id.</summary>
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
                    500, "CASH", 8_000),
                CancellationToken.None);
        Assert.True(finalized.IsSuccess, finalized.Error?.Message);
        return created.Value.ProductId;
    }

    [Fact]
    public async Task Detail_ShowsFinancialHeaderSessionCashierLinesAndPayment()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SellMangoAsync(bed);

        var result = await Sut(bed).ExecuteAsync(new GetSaleDetailRequest(1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var detail = result.Value;
        Assert.Equal(1, detail.SaleNumber);
        Assert.Equal(1, detail.CashSessionId);
        Assert.Equal("Admin", detail.CashierName);
        Assert.Equal(7_500, detail.SubtotalCop);
        Assert.Equal(500, detail.DiscountCop);
        Assert.Equal(7_000, detail.TotalCop);
        Assert.Equal("CASH", detail.PaymentMethod);
        Assert.Equal(8_000, detail.ReceivedCop);
        Assert.Equal(1_000, detail.ChangeCop);
        Assert.Equal("ACTIVE", detail.Status);
        Assert.Null(detail.VoidedByAdminName);

        var line = Assert.Single(detail.Lines);
        Assert.Equal(1, line.LineNumber);
        Assert.Equal("M1", line.Plu);
        Assert.Equal("Mango", line.Name);
        Assert.Equal("KG", line.UnitCode);
        Assert.Equal(1_250, line.QuantityMilli);
        Assert.Equal(6_000, line.UnitPriceCop);
        Assert.Equal(7_500, line.LineTotalCop);
    }

    [Fact]
    public async Task VoidedSale_ShowsAdministratorAndVoidMoment()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SellMangoAsync(bed);

        const long voidedAt = 1_800_000_000_000;
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
                + " VALUES (1, @VoidedAt, -300, 1, 'Admin');",
                new { VoidedAt = voidedAt });
        }

        var result = await Sut(bed).ExecuteAsync(new GetSaleDetailRequest(1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("VOIDED", result.Value.Status);
        Assert.Equal(voidedAt, result.Value.VoidedAtUtcMs);
        Assert.Equal(-300, result.Value.VoidedAtOffsetMinutes);
        Assert.Equal("Admin", result.Value.VoidedByAdminName);
    }

    [Fact]
    public async Task Cashier_IsDenied()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await Sut(bed, ProductTestBed.CashierSession()).ExecuteAsync(
            new GetSaleDetailRequest(1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }

    [Fact]
    public async Task UnknownSale_IsNotFound()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await Sut(bed).ExecuteAsync(new GetSaleDetailRequest(42), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
        Assert.Contains("V-000042", result.Error.Message);
    }

    [Fact]
    public async Task ProductEditedAfterSale_DetailKeepsTheSnapshots()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var productId = await SellMangoAsync(bed);

        var updated = await new UpdateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new UpdateProductRequest(productId, "M1", "Mango Tommy", ProductUnit.Kg, 9_999), CancellationToken.None);
        Assert.True(updated.IsSuccess, updated.Error?.Message);

        var result = await Sut(bed).ExecuteAsync(new GetSaleDetailRequest(1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal("Mango", line.Name);
        Assert.Equal(6_000, line.UnitPriceCop);
    }
}
