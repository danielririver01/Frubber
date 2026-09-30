using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Infrastructure.Persistence.Cash;

/// <summary>
/// Cash session persistence over Dapper with parameterized SQL (JHO-38).
/// Every method runs on the caller's <see cref="IUnitOfWork"/>; open/close
/// share the caller's BEGIN IMMEDIATE transaction so the single-open
/// invariant is atomic. Running totals come from valid (non-voided) sales;
/// the close writes the frozen snapshot the schema CHECK demands.
/// </summary>
public sealed class DapperCashSessionRepository : ICashSessionRepository
{
    public async Task<OpenCashSessionDto?> FindOpenAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT cs.CashSessionId, cs.OpenedAtUtcMs, cs.OpenedAtOffsetMinutes,"
            + " cs.OpenedByNameSnapshot, cs.OpeningBalanceCop,"
            + " COALESCE(SUM(CASE WHEN s.PaymentMethod = 'CASH' AND v.SaleId IS NULL THEN s.TotalCop ELSE 0 END), 0) AS CashSalesCop,"
            + " COALESCE(SUM(CASE WHEN s.PaymentMethod = 'TRANSFER' AND v.SaleId IS NULL THEN s.TotalCop ELSE 0 END), 0) AS TransferSalesCop,"
            + " COALESCE(SUM(CASE WHEN v.SaleId IS NULL THEN s.DiscountCop ELSE 0 END), 0) AS DiscountCop,"
            + " COALESCE(SUM(CASE WHEN v.SaleId IS NULL THEN 1 ELSE 0 END), 0) AS SaleCount"
            + " FROM CashSession cs"
            + " LEFT JOIN Sale s ON s.CashSessionId = cs.CashSessionId"
            + " LEFT JOIN SaleVoid v ON v.SaleId = s.SaleId"
            + " WHERE cs.ClosedAtUtcMs IS NULL"
            + " GROUP BY cs.CashSessionId;";
        var row = await unit.Connection.QuerySingleOrDefaultAsync<OpenRow>(sql, transaction: unit.Transaction);

        return row is null
            ? null
            : new OpenCashSessionDto(
                row.CashSessionId,
                row.OpenedAtUtcMs,
                row.OpenedAtOffsetMinutes,
                row.OpenedByNameSnapshot,
                row.OpeningBalanceCop,
                row.CashSalesCop,
                row.TransferSalesCop,
                row.DiscountCop,
                row.SaleCount);
    }

    public async Task<int> OpenAsync(
        IUnitOfWork unit,
        long openedAtUtcMs,
        int openedAtOffsetMinutes,
        int openedByUserId,
        string openedByNameSnapshot,
        long openingBalanceCop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "INSERT INTO CashSession"
            + " (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (@OpenedAt, @OffsetMin, @UserId, @UserName, @Balance);"
            + "SELECT last_insert_rowid();";
        return await unit.Connection.QuerySingleAsync<int>(
            sql,
            new
            {
                OpenedAt = openedAtUtcMs,
                OffsetMin = openedAtOffsetMinutes,
                UserId = openedByUserId,
                UserName = openedByNameSnapshot,
                Balance = openingBalanceCop
            },
            unit.Transaction);
    }

    public async Task<bool> CloseOpenAsync(
        IUnitOfWork unit,
        long closedAtUtcMs,
        int closedAtOffsetMinutes,
        int closedByUserId,
        string closedByNameSnapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        // One statement: freezes valid-sale totals; no-op when nothing open.
        const string sql = "UPDATE CashSession SET"
            + " ClosedAtUtcMs = @ClosedAt,"
            + " ClosedAtOffsetMinutes = @OffsetMin,"
            + " ClosedByUserId = @UserId,"
            + " ClosedByNameSnapshot = @UserName,"
            + " ClosedCashSalesCop = Totals.CashSales,"
            + " ClosedTransferSalesCop = Totals.TransferSales,"
            + " ClosedDiscountCop = Totals.Discount,"
            + " ClosedNetSalesCop = Totals.CashSales + Totals.TransferSales,"
            + " ClosedExpectedCashCop = OpeningBalanceCop + Totals.CashSales,"
            + " ClosedSaleCount = Totals.SaleCount"
            + " FROM ("
            + "   SELECT COALESCE(SUM(CASE WHEN s.PaymentMethod = 'CASH' AND v.SaleId IS NULL THEN s.TotalCop ELSE 0 END), 0) AS CashSales,"
            + "          COALESCE(SUM(CASE WHEN s.PaymentMethod = 'TRANSFER' AND v.SaleId IS NULL THEN s.TotalCop ELSE 0 END), 0) AS TransferSales,"
            + "          COALESCE(SUM(CASE WHEN v.SaleId IS NULL THEN s.DiscountCop ELSE 0 END), 0) AS Discount,"
            + "          COALESCE(SUM(CASE WHEN v.SaleId IS NULL THEN 1 ELSE 0 END), 0) AS SaleCount"
            + "   FROM CashSession cs"
            + "   LEFT JOIN Sale s ON s.CashSessionId = cs.CashSessionId"
            + "   LEFT JOIN SaleVoid v ON v.SaleId = s.SaleId"
            + "   WHERE cs.ClosedAtUtcMs IS NULL"
            + " ) AS Totals"
            + " WHERE ClosedAtUtcMs IS NULL;";
        var affected = await unit.Connection.ExecuteAsync(
            sql,
            new { ClosedAt = closedAtUtcMs, OffsetMin = closedAtOffsetMinutes, UserId = closedByUserId, UserName = closedByNameSnapshot },
            unit.Transaction);
        return affected > 0;
    }

    private sealed class OpenRow
    {
        public int CashSessionId { get; set; }

        public long OpenedAtUtcMs { get; set; }

        public int OpenedAtOffsetMinutes { get; set; }

        public string OpenedByNameSnapshot { get; set; } = string.Empty;

        public long OpeningBalanceCop { get; set; }

        public long CashSalesCop { get; set; }

        public long TransferSalesCop { get; set; }

        public long DiscountCop { get; set; }

        public int SaleCount { get; set; }
    }
}
