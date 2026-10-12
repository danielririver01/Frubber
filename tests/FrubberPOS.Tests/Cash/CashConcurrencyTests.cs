using Dapper;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Cash;

/// <summary>
/// Concurrency and invariant tests for cash sessions (JHO-37): single-open
/// race, idempotency, totals with both payment methods, void effect and
/// transactional integrity on failure.
/// </summary>
public sealed class CashConcurrencyTests
{
    [Fact]
    public async Task TwoConcurrentOpens_CreateOnlyOneSession()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var clock = bed.Clock;

        var openA = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), clock, bed.Gate);
        var openB = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), clock, bed.Gate);

        var results = await Task.WhenAll(
            openA.ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None),
            openB.ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.Equal(1, results.Count(r => r.IsFailure && r.Error!.Code == ErrorCode.Conflict));

        var get = new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession());
        var open = (await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None)).Value.Session;
        Assert.NotNull(open);
    }

    [Fact]
    public async Task GetOpen_IsIdempotent_ReadDoesNotChangeState()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        await open.ExecuteAsync(new OpenCashSessionRequest(10_000), CancellationToken.None);

        var get = new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession());
        var first = (await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None)).Value.Session;
        var second = (await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None)).Value.Session;

        Assert.Equal(first!.CashSessionId, second!.CashSessionId);
        Assert.Equal(first.OpeningBalanceCop, second.OpeningBalanceCop);
    }

    [Fact]
    public async Task Close_IsIdempotent_SecondCloseConflicts()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        await open.ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None);

        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        Assert.True((await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None)).IsSuccess);
        var again = await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None);
        Assert.False(again.IsSuccess);
    }

    [Fact]
    public async Task Totals_MatchBothPaymentMethodsAndVoidEffect()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var open = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        var opened = await open.ExecuteAsync(new OpenCashSessionRequest(100_000), CancellationToken.None);

        await SeedAsync(bed.DbPath, 1, opened.Value.CashSessionId, "CASH", 20_000, 1_000);
        await SeedAsync(bed.DbPath, 2, opened.Value.CashSessionId, "TRANSFER", 30_000, 0);
        await SeedAsync(bed.DbPath, 3, opened.Value.CashSessionId, "CASH", 5_000, 0);

        // Anular la venta 3: el esquema lo permite solo si la caja sigue abierta.
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
                + " VALUES (3, 5, 0, 1, 'Admin');");
        }

        var get = new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession());
        var s = (await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None)).Value.Session!;

        Assert.Equal(20_000, s.CashSalesCop);
        Assert.Equal(30_000, s.TransferSalesCop);
        Assert.Equal(1_000, s.DiscountCop);
        Assert.Equal(2, s.SaleCount);
        Assert.Equal(120_000, s.ExpectedCashCop);

        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        Assert.True((await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None)).IsSuccess);

        var summary = new GetCashSessionSummaryUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession());
        var frozen = (await summary.ExecuteAsync(new GetCashSessionSummaryRequest(opened.Value.CashSessionId), CancellationToken.None)).Value.Summary;
        Assert.True(frozen.IsClosed);
        Assert.Equal(20_000, frozen.CashSalesCop);
        Assert.Equal(30_000, frozen.TransferSalesCop);
        Assert.Equal(2, frozen.SaleCount);
        Assert.Equal(120_000, frozen.ExpectedCashCop);
    }

    [Fact]
    public async Task FailedTransaction_LeavesNoPartialState()
    {
        await using var bed = await CashTestBed.CreateAsync();

        // Salary negativo → falla antes de abrir; no queda ninguna caja abierta.
        var badOpen = new OpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        var failed = await badOpen.ExecuteAsync(new OpenCashSessionRequest(-100), CancellationToken.None);
        Assert.False(failed.IsSuccess);

        var get = new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession());
        Assert.Null((await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None)).Value.Session);

        // Cerrar sin caja → falla; y sigue sin caja abierta.
        var close = new CloseCashSessionUseCase(bed.Sessions, bed.Units, CashTestBed.AdminSession(), bed.Clock, bed.Gate);
        Assert.False((await close.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None)).IsSuccess);
        Assert.Null((await get.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None)).Value.Session);
    }

    private static async Task SeedAsync(string path, int saleId, int cashSessionId, string method, long total, long discount)
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
}
