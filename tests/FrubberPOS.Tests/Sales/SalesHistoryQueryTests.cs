using Dapper;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Sales;
using FrubberPOS.Tests.Products;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Sales;

/// <summary>
/// Paginated history query (JHO-47/75, RN-HIS-001/004): admin-only, dates
/// optional (both null = full history, half range rejected), newest-first,
/// exactly 50 per page except the last, and fast over 100.000 reference sales.
/// </summary>
public sealed class SalesHistoryQueryTests
{
    private static SearchSalesHistoryUseCase Sut(ProductTestBed bed, FrubberPOS.Tests.TestDoubles.FakeCurrentSession? session = null) =>
        new(bed.History, bed.Units, session ?? ProductTestBed.AdminSession());

    /// <summary>UTC ms de un momento local: la prueba vale en cualquier zona horaria.</summary>
    private static long LocalMs(int year, int month, int day, int hour = 12, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUnixTimeMilliseconds();
    }

    private static async Task SeedUserAndCashAsync(ProductTestBed bed)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@UserId, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { UserId = 1, Salt = new byte[16], Hash = new byte[32] });
        await connection.ExecuteAsync(
            "INSERT INTO CashSession (CashSessionId, OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1, 1, 0, 1, 'Admin', 0);");
    }

    private static async Task SeedSaleAsync(
        ProductTestBed bed, long saleNumber, long soldAtUtcMs, string paymentMethod = "CASH",
        long totalCop = 1_000, string cashier = "Admin", bool voided = false)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        var isCash = paymentMethod == "CASH";
        await connection.ExecuteAsync(
            "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
            + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (@SaleNumber, 1, 1, @Cashier, @SoldAtUtcMs, 0, @TotalCop, 0, @TotalCop, @PaymentMethod,"
            + " @ReceivedCop, @ChangeCop);",
            new
            {
                SaleNumber = saleNumber,
                Cashier = cashier,
                SoldAtUtcMs = soldAtUtcMs,
                TotalCop = totalCop,
                PaymentMethod = paymentMethod,
                ReceivedCop = isCash ? totalCop : (long?)null,
                ChangeCop = isCash ? 0L : (long?)null
            });
        if (voided)
        {
            await connection.ExecuteAsync(
                "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
                + " VALUES ((SELECT SaleId FROM Sale WHERE SaleNumber = @SaleNumber), @VoidedAt, 0, 1, 'Admin');",
                new { SaleNumber = saleNumber, VoidedAt = soldAtUtcMs + 1 });
        }
    }

    [Fact]
    public async Task NullDates_ReturnTheWholeHistory()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedUserAndCashAsync(bed);
        await SeedSaleAsync(bed, 1, LocalMs(2026, 8, 15));
        await SeedSaleAsync(bed, 2, LocalMs(2026, 9, 30));
        await SeedSaleAsync(bed, 3, LocalMs(2026, 10, 2));

        var result = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(null, null, 1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal([3L, 2L, 1L], result.Value.Items.Select(item => item.SaleNumber).ToArray());
    }

    [Fact]
    public async Task HalfRange_IsRejected()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var fromOnly = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), null, 1), CancellationToken.None);
        var toOnly = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(null, new DateOnly(2026, 9, 30), 1), CancellationToken.None);

        Assert.True(fromOnly.IsFailure);
        Assert.Equal(ErrorCode.Validation, fromOnly.Error!.Code);
        Assert.Contains("Complete ambas fechas", fromOnly.Error.Message);
        Assert.True(toOnly.IsFailure);
        Assert.Contains("Complete ambas fechas", toOnly.Error!.Message);
    }

    [Fact]
    public async Task EndBeforeStart_IsRejected()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 29), 1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Hasta no puede ser anterior a Desde", result.Error!.Message);
    }

    [Fact]
    public async Task InvalidPage_IsRejected()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("página", result.Error!.Message);
    }

    [Fact]
    public async Task Cashier_IsDenied()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await Sut(bed, ProductTestBed.CashierSession()).ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
        Assert.Equal(FrubberPOS.Application.Authentication.Authorization.DeniedMessage, result.Error.Message);
    }

    [Fact]
    public async Task RangeFilters_IncludingWholeEndDay_AndOrdersNewestFirst()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedUserAndCashAsync(bed);
        // Horas locales: caen en días calendario conocidos sin importar la zona.
        await SeedSaleAsync(bed, 1, LocalMs(2026, 9, 28));
        await SeedSaleAsync(bed, 2, LocalMs(2026, 9, 30));
        await SeedSaleAsync(bed, 3, LocalMs(2026, 9, 30, 13)); // más tarde el mismo día
        await SeedSaleAsync(bed, 4, LocalMs(2026, 10, 2));    // fuera del rango

        var result = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal([3L, 2L], result.Value.Items.Select(item => item.SaleNumber).ToArray());
    }

    [Fact]
    public async Task Row_CarriesNumberMomentCashierPaymentTotalAndStatus()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedUserAndCashAsync(bed);
        var moment = LocalMs(2026, 9, 30, 12, 5);
        await SeedSaleAsync(bed, 7, LocalMs(2026, 9, 30), "TRANSFER", 8_400, "Cajero Uno");
        await SeedSaleAsync(bed, 8, moment, "CASH", 2_000, "Admin", voided: true);

        var result = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var voided = result.Value.Items.Single(item => item.SaleNumber == 8);
        Assert.Equal("VOIDED", voided.Status);
        Assert.Equal("CASH", voided.PaymentMethod);
        Assert.Equal(2_000, voided.TotalCop);
        Assert.Equal("Admin", voided.CashierName);
        Assert.Equal(moment, voided.SoldAtUtcMs);

        var active = result.Value.Items.Single(item => item.SaleNumber == 7);
        Assert.Equal("ACTIVE", active.Status);
        Assert.Equal("TRANSFER", active.PaymentMethod);
        Assert.Equal("Cajero Uno", active.CashierName);
    }

    [Fact]
    public async Task Pages_ReturnExactlyFiftyExceptTheLast()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedUserAndCashAsync(bed);
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            var baseMs = LocalMs(2026, 9, 30);
            for (long number = 1; number <= 120; number++)
            {
                await connection.ExecuteAsync(
                    "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
                    + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
                    + " VALUES (@Number, 1, 1, 'Admin', @Moment, 0, 1000, 0, 1000, 'CASH', 1000, 0);",
                    new { Number = number, Moment = baseMs + number * 1_000 });
            }
        }

        var page2 = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 2), CancellationToken.None);
        var page3 = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 3), CancellationToken.None);

        Assert.Equal(50, page2.Value.Items.Count);
        Assert.Equal(120, page2.Value.TotalCount);
        Assert.Equal(20, page3.Value.Items.Count);
        // La página 2 arranca justo donde terminó la 1 (más reciente primero).
        var page1 = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 1), CancellationToken.None);
        Assert.All(page1.Value.Items.Concat(page2.Value.Items).Concat(page3.Value.Items)
            .Zip(page1.Value.Items.Concat(page2.Value.Items).Concat(page3.Value.Items).Skip(1)),
            pair => Assert.True(pair.First.SaleNumber > pair.Second.SaleNumber));
    }

    [Fact]
    public async Task ReferenceVolume_100kSales_QueriesFastAndCorrect()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedUserAndCashAsync(bed);
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            // 100.000 ventas generadas en una sola sentencia (CTE recursivo).
            await connection.ExecuteAsync(@"
WITH RECURSIVE seq(n) AS (
    SELECT 1
    UNION ALL SELECT n + 1 FROM seq WHERE n < 100000
)
INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,
    SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)
SELECT n, 1, 1, 'Admin', @Base + n, -300, 1000, 0, 1000, 'CASH', 1000, 0
FROM seq;",
                new { Base = LocalMs(2026, 9, 30) });
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await Sut(bed).ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 1), CancellationToken.None);
        stopwatch.Stop();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(100_000, result.Value.TotalCount);
        Assert.Equal(50, result.Value.Items.Count);
        Assert.Equal(100_000, result.Value.Items[0].SaleNumber); // más reciente primero
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"La consulta tardó {stopwatch.Elapsed}.");
    }
}
