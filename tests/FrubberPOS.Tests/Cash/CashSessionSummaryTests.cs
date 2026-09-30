using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Cash;

/// <summary>
/// Integration tests for the financial summary (JHO-35): real DB, real
/// sales and voids, open vs closed sessions.
/// </summary>
public sealed class CashSessionSummaryTests
{
    private static async Task SeedSaleAsync(string path, int saleId, int cashSessionId, string method, long total, long discount)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO Sale (SaleId, SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
            + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (@Id, @Id, @CashSessionId, 1, 'Admin', 2, 0, @Subtotal, @Discount, @Total, @Method, @Received, @Change);",
            new
            {
                Id = saleId,
                CashSessionId = cashSessionId,
                Subtotal = total + discount,
                Discount = discount,
                Total = total,
                Method = method,
                Received = method == "CASH" ? total : (long?)null,
                Change = method == "CASH" ? 0 : (long?)null
            });
    }

    private static async Task VoidSaleAsync(string path, int saleId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
            + " VALUES (@SaleId, 5, 0, 1, 'Admin');",
            new { SaleId = saleId });
    }

    private static GetCashSessionSummaryUseCase UseCase(CashTestBed bed) =>
        new(bed.Sessions, bed.Units, CashTestBed.AdminSession());

    [Fact]
    public async Task Summary_OpenSession_ComputesRunningTotalsExcludingVoided()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock);
        var opened = await open.ExecuteAsync(new OpenCashSessionRequest(100_000), CancellationToken.None);
        var path = bed.DbPath;
        await SeedSaleAsync(path, 1, opened.Value.CashSessionId, "CASH", 20_000, 1_000);
        await SeedSaleAsync(path, 2, opened.Value.CashSessionId, "TRANSFER", 30_000, 0);
        await SeedSaleAsync(path, 3, opened.Value.CashSessionId, "CASH", 10_000, 0);
        await VoidSaleAsync(path, 3);

        var result = await UseCase(bed).ExecuteAsync(new GetCashSessionSummaryRequest(opened.Value.CashSessionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var s = result.Value.Summary;
        Assert.False(s.IsClosed);
        Assert.Equal(100_000, s.OpeningBalanceCop);
        Assert.Equal(20_000, s.CashSalesCop);
        Assert.Equal(30_000, s.TransferSalesCop);
        Assert.Equal(1_000, s.DiscountCop);
        Assert.Equal(50_000, s.NetSalesCop);
        Assert.Equal(120_000, s.ExpectedCashCop);
        Assert.Equal(2, s.SaleCount);
    }

    [Fact]
    public async Task Summary_ClosedSession_FreezesValidTotals()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock);
        var opened = await open.ExecuteAsync(new OpenCashSessionRequest(100_000), CancellationToken.None);
        await SeedSaleAsync(bed.DbPath, 1, opened.Value.CashSessionId, "CASH", 20_000, 1_000);
        await SeedSaleAsync(bed.DbPath, 2, opened.Value.CashSessionId, "TRANSFER", 30_000, 0);

        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock);
        Assert.True((await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None)).IsSuccess);

        var s = (await UseCase(bed).ExecuteAsync(new GetCashSessionSummaryRequest(opened.Value.CashSessionId), CancellationToken.None)).Value.Summary;
        Assert.True(s.IsClosed);
        Assert.Equal(20_000, s.CashSalesCop);
        Assert.Equal(30_000, s.TransferSalesCop);
        Assert.Equal(50_000, s.NetSalesCop);
        Assert.Equal(120_000, s.ExpectedCashCop);
        Assert.Equal(2, s.SaleCount);
    }

    [Fact]
    public async Task Summary_UnknownSession_NotFound()
    {
        await using var bed = await CashTestBed.CreateAsync();

        var result = await UseCase(bed).ExecuteAsync(new GetCashSessionSummaryRequest(999), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Summary_WithoutSession_Authorization()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock);
        var opened = await open.ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None);

        var useCase = new GetCashSessionSummaryUseCase(bed.Sessions, bed.Units, new TestDoubles.FakeCurrentSession());
        var result = await useCase.ExecuteAsync(new GetCashSessionSummaryRequest(opened.Value.CashSessionId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
    }
}
