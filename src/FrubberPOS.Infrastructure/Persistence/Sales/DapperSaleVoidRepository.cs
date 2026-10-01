using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Sales;

namespace FrubberPOS.Infrastructure.Persistence.Sales;

/// <summary>
/// Void persistence over Dapper with parameterized SQL (JHO-49). Runs on
/// the caller's unit of work (BEGIN IMMEDIATE): the void mark and the
/// stock reintegration commit or roll back together. <c>SaleVoid</c>'s
/// primary key makes a duplicate void impossible even under races.
/// </summary>
public sealed class DapperSaleVoidRepository : ISaleVoidRepository
{
    public async Task<SaleVoidContext?> FindVoidContextAsync(IUnitOfWork unit, long saleNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT s.SaleId, s.CashSessionId,"
            + " CASE WHEN sv.SaleId IS NULL THEN 0 ELSE 1 END AS IsVoided,"
            + " CASE WHEN cs.ClosedAtUtcMs IS NULL THEN 1 ELSE 0 END AS CashSessionOpen"
            + " FROM Sale AS s"
            + " LEFT JOIN SaleVoid AS sv ON sv.SaleId = s.SaleId"
            + " JOIN CashSession AS cs ON cs.CashSessionId = s.CashSessionId"
            + " WHERE s.SaleNumber = @SaleNumber;";
        var row = await unit.Connection.QuerySingleOrDefaultAsync<SaleVoidContextRow>(
            new CommandDefinition(sql, new { SaleNumber = saleNumber }, unit.Transaction, cancellationToken: cancellationToken));
        return row is null
            ? null
            : new SaleVoidContext(row.SaleId, row.CashSessionId, row.IsVoided, row.CashSessionOpen);
    }

    public async Task<IReadOnlyList<SaleVoidLine>> GetLinesForVoidAsync(IUnitOfWork unit, int saleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT ProductId, QuantityMilli FROM SaleLine WHERE SaleId = @SaleId ORDER BY LineNumber;";
        var rows = await unit.Connection.QueryAsync<SaleVoidLineRow>(
            new CommandDefinition(sql, new { SaleId = saleId }, unit.Transaction, cancellationToken: cancellationToken));
        return rows.Select(row => new SaleVoidLine(row.ProductId, row.QuantityMilli)).ToList();
    }

    public async Task InsertVoidAsync(
        IUnitOfWork unit,
        int saleId,
        long voidedAtUtcMs,
        int voidedAtOffsetMinutes,
        int adminUserId,
        string adminNameSnapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
            + " VALUES (@SaleId, @VoidedAtUtcMs, @VoidedAtOffsetMinutes, @AdminUserId, @AdminNameSnapshot);";
        await unit.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    SaleId = saleId,
                    VoidedAtUtcMs = voidedAtUtcMs,
                    VoidedAtOffsetMinutes = voidedAtOffsetMinutes,
                    AdminUserId = adminUserId,
                    AdminNameSnapshot = adminNameSnapshot
                },
                unit.Transaction,
                cancellationToken: cancellationToken));
    }

    private sealed class SaleVoidContextRow
    {
        public int SaleId { get; set; }

        public int CashSessionId { get; set; }

        public bool IsVoided { get; set; }

        public bool CashSessionOpen { get; set; }
    }

    private sealed class SaleVoidLineRow
    {
        public int ProductId { get; set; }

        public long QuantityMilli { get; set; }
    }
}
