using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Sales;

namespace FrubberPOS.Infrastructure.Persistence.Sales;

/// <summary>
/// History reads over the approved <c>V_SaleHistory</c> view (JHO-47):
/// half-open UTC range backed by <c>IX_Sale_SoldAt</c>, newest first
/// (ties break by SaleId), page via LIMIT/OFFSET. All SQL parameterized.
/// </summary>
public sealed class DapperSaleHistoryRepository : ISaleHistoryRepository
{
    public async Task<SaleHistoryPage> SearchAsync(
        IUnitOfWork unit,
        long fromUtcMs,
        long toUtcMsExclusive,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string countSql = "SELECT COUNT(*) FROM V_SaleHistory"
            + " WHERE SoldAtUtcMs >= @FromUtcMs AND SoldAtUtcMs < @ToUtcMsExclusive;";
        const string pageSql = "SELECT SaleNumber, SoldAtUtcMs, SoldAtOffsetMinutes, CashierNameSnapshot AS CashierName,"
            + " PaymentMethod, TotalCop, SaleStatus AS Status"
            + " FROM V_SaleHistory"
            + " WHERE SoldAtUtcMs >= @FromUtcMs AND SoldAtUtcMs < @ToUtcMsExclusive"
            + " ORDER BY SoldAtUtcMs DESC, SaleNumber DESC"
            + " LIMIT @Take OFFSET @Skip;";
        var parameters = new
        {
            FromUtcMs = fromUtcMs,
            ToUtcMsExclusive = toUtcMsExclusive,
            Skip = skip,
            Take = take
        };

        var total = await unit.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, unit.Transaction, cancellationToken: cancellationToken));
        var rows = await unit.Connection.QueryAsync<SaleHistoryItemRow>(
            new CommandDefinition(pageSql, parameters, unit.Transaction, cancellationToken: cancellationToken));

        return new SaleHistoryPage(
            rows.Select(row => (SaleHistoryItemDto)row).ToList(), total);
    }

    /// <summary>Full historical detail; <c>null</c> when the number is unknown.</summary>
    public async Task<SaleDetailDto?> FindDetailAsync(IUnitOfWork unit, long saleNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string headerSql = "SELECT SaleId, SaleNumber, CashSessionId, CashierNameSnapshot AS CashierName,"
            + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod,"
            + " ReceivedCop, ChangeCop, SaleStatus AS Status,"
            + " VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminNameSnapshot AS VoidedByAdminName"
            + " FROM V_SaleHistory WHERE SaleNumber = @SaleNumber;";
        var header = await unit.Connection.QuerySingleOrDefaultAsync<SaleDetailHeaderRow>(
            new CommandDefinition(headerSql, new { SaleNumber = saleNumber }, unit.Transaction, cancellationToken: cancellationToken));
        if (header is null)
        {
            return null;
        }

        const string linesSql = "SELECT LineNumber, PluSnapshot AS Plu, ProductNameSnapshot AS Name,"
            + " UnitSnapshot AS UnitCode, QuantityMilli, UnitPriceCopSnapshot AS UnitPriceCop, LineTotalCop"
            + " FROM SaleLine WHERE SaleId = @SaleId ORDER BY LineNumber;";
        var lines = await unit.Connection.QueryAsync<SaleDetailLineRow>(
            new CommandDefinition(linesSql, new { header.SaleId }, unit.Transaction, cancellationToken: cancellationToken));

        return new SaleDetailDto(
            header.SaleNumber,
            header.CashSessionId,
            header.CashierName,
            header.SoldAtUtcMs,
            header.SoldAtOffsetMinutes,
            header.SubtotalCop,
            header.DiscountCop,
            header.TotalCop,
            header.PaymentMethod,
            header.ReceivedCop,
            header.ChangeCop,
            header.Status,
            header.VoidedAtUtcMs,
            header.VoidedAtOffsetMinutes,
            header.VoidedByAdminName,
            lines.Select(line => new SaleDetailLineDto(
                line.LineNumber, line.Plu, line.Name, line.UnitCode,
                line.QuantityMilli, line.UnitPriceCop, line.LineTotalCop))
                .ToList());
    }

    private sealed class SaleHistoryItemRow
    {
        public long SaleNumber { get; set; }

        public long SoldAtUtcMs { get; set; }

        public int SoldAtOffsetMinutes { get; set; }

        public string CashierName { get; set; } = string.Empty;

        public string PaymentMethod { get; set; } = string.Empty;

        public long TotalCop { get; set; }

        public string Status { get; set; } = string.Empty;

        public static implicit operator SaleHistoryItemDto(SaleHistoryItemRow row) => new(
            row.SaleNumber, row.SoldAtUtcMs, row.SoldAtOffsetMinutes,
            row.CashierName, row.PaymentMethod, row.TotalCop, row.Status);
    }

    private sealed class SaleDetailHeaderRow
    {
        public int SaleId { get; set; }

        public long SaleNumber { get; set; }

        public int CashSessionId { get; set; }

        public string CashierName { get; set; } = string.Empty;

        public long SoldAtUtcMs { get; set; }

        public int SoldAtOffsetMinutes { get; set; }

        public long SubtotalCop { get; set; }

        public long DiscountCop { get; set; }

        public long TotalCop { get; set; }

        public string PaymentMethod { get; set; } = string.Empty;

        public long? ReceivedCop { get; set; }

        public long? ChangeCop { get; set; }

        public string Status { get; set; } = string.Empty;

        public long? VoidedAtUtcMs { get; set; }

        public int? VoidedAtOffsetMinutes { get; set; }

        public string? VoidedByAdminName { get; set; }
    }

    private sealed class SaleDetailLineRow
    {
        public int LineNumber { get; set; }

        public string Plu { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string UnitCode { get; set; } = string.Empty;

        public long QuantityMilli { get; set; }

        public long UnitPriceCop { get; set; }

        public long LineTotalCop { get; set; }
    }
}
