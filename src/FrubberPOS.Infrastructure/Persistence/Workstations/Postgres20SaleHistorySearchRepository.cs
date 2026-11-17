using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Sales;
using Npgsql;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

public sealed class Postgres20SaleHistorySearchRepository(Postgres20WorkstationStore? workstation = null) : ISaleHistoryRepository
{
    public async Task<SaleDetailDto?> FindDetailAsync(
        IUnitOfWork unit, long saleNumber, CancellationToken cancellationToken)
    {
        if (workstation?.AdminToken is not { } token)
        {
            throw new UnauthorizedAccessException();
        }

        const string sql = """
            SELECT line_number AS LineNumber, plu, name, unit_code AS UnitCode,
                   quantity_milli AS QuantityMilli, unit_price_cop AS UnitPriceCop, line_total_cop AS LineTotalCop
            FROM public.app_admin_sale_lines(@Token, @SaleNumber);
            SELECT cash_session_id::integer AS CashSessionId, cashier_name_snapshot AS CashierName,
                   sold_at AS SoldAt, subtotal_cop AS SubtotalCop, discount_cop AS DiscountCop,
                   total_cop AS TotalCop, payment_method AS PaymentMethod, received_cop AS ReceivedCop,
                   change_cop AS ChangeCop, sale_status AS Status, voided_at AS VoidedAt,
                   voided_by_name_snapshot AS VoidedBy
            FROM public.v_sale_history WHERE sale_number = @SaleNumber;
            """;
        try
        {
            using var results = await unit.Connection.QueryMultipleAsync(new CommandDefinition(
                sql, new { Token = token, SaleNumber = saleNumber }, unit.Transaction,
                cancellationToken: cancellationToken));
            var lines = (await results.ReadAsync<SaleDetailLineDto>()).AsList();
            var row = await results.ReadSingleOrDefaultAsync<DetailRow>();
            if (row is null)
            {
                return null;
            }

            var soldAt = new DateTimeOffset(row.SoldAt, TimeSpan.Zero);
            DateTimeOffset? voidedAt = row.VoidedAt is { } utc ? new DateTimeOffset(utc, TimeSpan.Zero) : null;
            return new SaleDetailDto(saleNumber, row.CashSessionId, row.CashierName, soldAt.ToUnixTimeMilliseconds(),
                OffsetMinutes(soldAt), row.SubtotalCop, row.DiscountCop, row.TotalCop, row.PaymentMethod,
                row.ReceivedCop, row.ChangeCop, row.Status, false, voidedAt?.ToUnixTimeMilliseconds(),
                voidedAt is { } instant ? OffsetMinutes(instant) : null, row.VoidedBy, lines);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw new UnauthorizedAccessException("Inicie sesión nuevamente como administrador.", ex);
        }
        catch (NpgsqlException ex) when (ex.IsTransient)
        {
            throw new DatabaseUnavailableException(ex);
        }
    }

    private static int OffsetMinutes(DateTimeOffset instant) =>
        (int)TimeZoneInfo.Local.GetUtcOffset(instant).TotalMinutes;

    private sealed record DetailRow(int CashSessionId, string CashierName, DateTime SoldAt,
        long SubtotalCop, long DiscountCop, long TotalCop, string PaymentMethod, long? ReceivedCop,
        long? ChangeCop, string Status, DateTime? VoidedAt, string? VoidedBy);

    public async Task<SaleHistoryPage> SearchAsync(
        IUnitOfWork unit, long fromUtcMs, long toUtcMsExclusive,
        int skip, int take, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH filtered AS (
                SELECT sale_number, sold_at, cashier_name_snapshot, payment_method, total_cop, sale_status
                FROM public.v_sale_history
                WHERE (CAST(@FromUtc AS timestamptz) IS NULL OR sold_at >= CAST(@FromUtc AS timestamptz))
                  AND (CAST(@ToUtc AS timestamptz) IS NULL OR sold_at < CAST(@ToUtc AS timestamptz))
            ), page AS (
                SELECT * FROM filtered
                ORDER BY sold_at DESC, sale_number DESC
                LIMIT @Take OFFSET @Skip
            )
            SELECT totals.total_count AS TotalCount, page.sale_number AS SaleNumber,
                   page.sold_at AS SoldAt, page.cashier_name_snapshot AS CashierName,
                   page.payment_method AS PaymentMethod, page.total_cop AS TotalCop,
                   page.sale_status AS Status
            FROM (SELECT count(*)::integer AS total_count FROM filtered) totals
            LEFT JOIN page ON true
            ORDER BY page.sold_at DESC, page.sale_number DESC;
            """;
        var arguments = new
        {
            FromUtc = fromUtcMs == 0 ? (DateTime?)null : DateTimeOffset.FromUnixTimeMilliseconds(fromUtcMs).UtcDateTime,
            ToUtc = toUtcMsExclusive == long.MaxValue ? (DateTime?)null : DateTimeOffset.FromUnixTimeMilliseconds(toUtcMsExclusive).UtcDateTime,
            Skip = skip,
            Take = take
        };
        List<HistoryRow> rows;
        try
        {
            rows = (await unit.Connection.QueryAsync<HistoryRow>(new CommandDefinition(
                sql, arguments, unit.Transaction, cancellationToken: cancellationToken))).AsList();
        }
        catch (NpgsqlException ex) when (ex.IsTransient)
        {
            throw new DatabaseUnavailableException(ex);
        }
        var items = rows.Where(row => row.SaleNumber.HasValue).Select(row =>
        {
            var instant = new DateTimeOffset(row.SoldAt, TimeSpan.Zero);
            return new SaleHistoryItemDto(row.SaleNumber!.Value, instant.ToUnixTimeMilliseconds(),
                (int)TimeZoneInfo.Local.GetUtcOffset(instant).TotalMinutes, row.CashierName,
                row.PaymentMethod, row.TotalCop, row.Status);
        }).ToArray();
        return new SaleHistoryPage(items, rows[0].TotalCount);
    }

    private sealed class HistoryRow
    {
        public int TotalCount { get; init; }
        public long? SaleNumber { get; init; }
        public DateTime SoldAt { get; init; }
        public string CashierName { get; init; } = string.Empty;
        public string PaymentMethod { get; init; } = string.Empty;
        public long TotalCop { get; init; }
        public string Status { get; init; } = string.Empty;
    }
}
