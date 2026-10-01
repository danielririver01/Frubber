using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Sales;

namespace FrubberPOS.Infrastructure.Persistence.Sales;

/// <summary>
/// Sale persistence over Dapper with parameterized SQL (JHO-43). Runs on
/// the caller's unit of work (BEGIN IMMEDIATE): sequence, header, lines
/// and stock adjustments never leak on rollback.
/// </summary>
public sealed class DapperSaleRepository : ISaleRepository
{
    public async Task<long> NextSaleNumberAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "UPDATE SystemSequence SET LastValue = LastValue + 1"
            + " WHERE SequenceName = 'SaleNumber' RETURNING LastValue;";
        return await unit.Connection.QuerySingleAsync<long>(sql, transaction: unit.Transaction);
    }

    public async Task<int> InsertSaleAsync(
        IUnitOfWork unit,
        long saleNumber,
        int cashSessionId,
        int cashierUserId,
        string cashierNameSnapshot,
        long soldAtUtcMs,
        int soldAtOffsetMinutes,
        long subtotalCop,
        long discountCop,
        string paymentMethod,
        long? receivedCop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
            + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (@SaleNumber, @CashSessionId, @CashierUserId, @CashierNameSnapshot,"
            + " @SoldAtUtcMs, @SoldAtOffsetMinutes, @SubtotalCop, @DiscountCop, @TotalCop, @PaymentMethod, @ReceivedCop, @ChangeCop);"
            + "SELECT last_insert_rowid();";
        long totalCop = subtotalCop - discountCop;
        long? changeCop = paymentMethod == "CASH" && receivedCop is not null ? receivedCop - totalCop : null;

        return await unit.Connection.QuerySingleAsync<int>(
            sql,
            new
            {
                SaleNumber = saleNumber,
                CashSessionId = cashSessionId,
                CashierUserId = cashierUserId,
                CashierNameSnapshot = cashierNameSnapshot,
                SoldAtUtcMs = soldAtUtcMs,
                SoldAtOffsetMinutes = soldAtOffsetMinutes,
                SubtotalCop = subtotalCop,
                DiscountCop = discountCop,
                TotalCop = totalCop,
                PaymentMethod = paymentMethod,
                ReceivedCop = receivedCop,
                ChangeCop = changeCop
            },
            unit.Transaction);
    }

    public async Task InsertLineAsync(IUnitOfWork unit, int saleId, int lineNumber, SaleLinePersist line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(line);

        const string sql = "INSERT INTO SaleLine (SaleId, LineNumber, ProductId, PluSnapshot, ProductNameSnapshot, UnitSnapshot,"
            + " UnitPriceCopSnapshot, QuantityMilli, LineTotalCop)"
            + " VALUES (@SaleId, @LineNumber, @ProductId, @PluSnapshot, @ProductNameSnapshot, @UnitSnapshot,"
            + " @UnitPriceCopSnapshot, @QuantityMilli, @LineTotalCop);";
        var lineTotalCop = (line.UnitPriceCop * line.QuantityMilli + 500) / 1000;

        await unit.Connection.ExecuteAsync(
            sql,
            new
            {
                SaleId = saleId,
                LineNumber = lineNumber,
                ProductId = line.ProductId,
                PluSnapshot = line.PluSnapshot,
                ProductNameSnapshot = line.NameSnapshot,
                UnitSnapshot = line.UnitSnapshot,
                UnitPriceCopSnapshot = line.UnitPriceCop,
                QuantityMilli = line.QuantityMilli,
                LineTotalCop = lineTotalCop
            },
            unit.Transaction);
    }

    public async Task AdjustStockAsync(IUnitOfWork unit, int productId, long deltaMilli, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "UPDATE Product"
            + " SET StockMilli = StockMilli + @DeltaMilli, UnitLocked = 1"
            + " WHERE ProductId = @ProductId;";
        await unit.Connection.ExecuteAsync(
            sql, new { DeltaMilli = deltaMilli, ProductId = productId }, unit.Transaction);
    }

    public async Task<ProductPriceRow?> FindProductRowAsync(IUnitOfWork unit, int productId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT ProductId, Plu, Name, Unit, PriceCop, StockMilli, IsActive FROM Product WHERE ProductId = @ProductId;";
        return await unit.Connection.QuerySingleOrDefaultAsync<ProductPriceRow>(sql, new { ProductId = productId }, unit.Transaction);
    }
}
