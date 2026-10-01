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
}
