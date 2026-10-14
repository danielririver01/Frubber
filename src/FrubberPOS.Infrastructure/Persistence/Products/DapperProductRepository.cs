using System.Globalization;
using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Infrastructure.Persistence.Products;

/// <summary>
/// Product persistence over Dapper with parameterized SQL (JHO-32).
/// Stateless: every method works on the caller's <see cref="IUnitOfWork"/>
/// so reads and the PLU uniqueness re-check share one connection and one
/// transaction. Friendly duplicate-PLU checks live in use cases; the
/// table's unique index is the backstop.
/// </summary>
public sealed class DapperProductRepository : IProductRepository
{
    private const string Columns = "ProductId, Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked,"
        + " CASE WHEN EXISTS (SELECT 1 FROM SaleLine sl WHERE sl.ProductId = Product.ProductId) THEN 1 ELSE 0 END AS HasSalesHistory";

    public async Task<ProductDto?> FindByIdAsync(IUnitOfWork unit, int productId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        var sql = $"SELECT {Columns} FROM Product WHERE ProductId = @ProductId;";
        var row = await unit.Connection.QuerySingleOrDefaultAsync<ProductRow>(sql, new { ProductId = productId }, unit.Transaction);

        return row is null ? null : Map(row);
    }

    public async Task<ProductDto?> FindByPluAsync(IUnitOfWork unit, string plu, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        var sql = $"SELECT {Columns} FROM Product WHERE Plu = @Plu;";
        var row = await unit.Connection.QuerySingleOrDefaultAsync<ProductRow>(sql, new { Plu = plu }, unit.Transaction);

        return row is null ? null : Map(row);
    }

    public async Task<bool> PluExistsAsync(IUnitOfWork unit, string plu, int? excludeProductId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT COUNT(*) FROM Product"
            + " WHERE Plu = @Plu AND (@Exclude IS NULL OR ProductId <> @Exclude);";
        var count = await unit.Connection.QuerySingleAsync<long>(
            sql, new { Plu = plu, Exclude = (int?)excludeProductId }, unit.Transaction);
        return count > 0;
    }

    public async Task<string> GetNextPluAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        // JHO-87: numeric PLUs only — CAST turns 'ABC'/'M1' into 0, so an
        // alphanumeric PLU never raises the suggestion past the true max.
        const string sql = "SELECT COALESCE(MAX(CAST(Plu AS INTEGER)), 0) + 1 FROM Product;";
        var next = await unit.Connection.QuerySingleAsync<long>(sql, transaction: unit.Transaction);
        return next.ToString(CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<ProductDto>> SearchAsync(IUnitOfWork unit, string term, ProductStatusFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        // LIKE wildcards in the user's term are data, not pattern syntax.
        var escaped = term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        var sql = $"SELECT {Columns} FROM Product"
            + " WHERE (@Filter = 0 OR (@Filter = 1 AND IsActive = 1) OR (@Filter = 2 AND IsActive = 0))"
            + " AND (@Term = '' OR Plu LIKE @Prefix ESCAPE '\\' OR Name LIKE @Contains ESCAPE '\\')"
            + " ORDER BY IsActive DESC, Name COLLATE NOCASE"
            + " LIMIT 500;";
        var rows = await unit.Connection.QueryAsync<ProductRow>(
            sql,
            new
            {
                Filter = (int)filter,
                Term = term,
                Prefix = escaped + "%",
                Contains = "%" + escaped + "%"
            },
            unit.Transaction);

        return rows.Select(Map).ToList();
    }

    public async Task<int> CreateAsync(
        IUnitOfWork unit,
        string plu,
        string name,
        ProductUnit productUnit,
        long priceCop,
        long stockMilli,
        bool unitLocked,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "INSERT INTO Product (Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)"
            + " VALUES (@Plu, @Name, @Unit, @PriceCop, @StockMilli, 1, @UnitLocked);"
            + "SELECT last_insert_rowid();";
        return await unit.Connection.QuerySingleAsync<int>(
            sql,
            new
            {
                Plu = plu,
                Name = name,
                Unit = ProductUnits.ToCode(productUnit),
                PriceCop = priceCop,
                StockMilli = stockMilli,
                UnitLocked = unitLocked ? 1 : 0
            },
            unit.Transaction);
    }

    public async Task UpdateAsync(
        IUnitOfWork unit,
        int productId,
        string plu,
        string name,
        ProductUnit productUnit,
        long priceCop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "UPDATE Product SET Plu = @Plu, Name = @Name, Unit = @Unit, PriceCop = @PriceCop"
            + " WHERE ProductId = @ProductId;";
        await unit.Connection.ExecuteAsync(
            sql,
            new { Plu = plu, Name = name, Unit = ProductUnits.ToCode(productUnit), PriceCop = priceCop, ProductId = productId },
            unit.Transaction);
    }

    public async Task SetActiveAsync(IUnitOfWork unit, int productId, bool isActive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "UPDATE Product SET IsActive = @Active WHERE ProductId = @ProductId;";
        await unit.Connection.ExecuteAsync(sql, new { Active = isActive ? 1 : 0, ProductId = productId }, unit.Transaction);
    }

    public async Task SetStockAsync(IUnitOfWork unit, int productId, long newStockMilli, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "UPDATE Product SET StockMilli = @Stock,"
            + " UnitLocked = CASE WHEN @Stock <> 0 THEN 1 ELSE UnitLocked END"
            + " WHERE ProductId = @ProductId;";
        await unit.Connection.ExecuteAsync(sql, new { Stock = newStockMilli, ProductId = productId }, unit.Transaction);
    }

    public async Task DeleteAsync(IUnitOfWork unit, int productId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "DELETE FROM Product WHERE ProductId = @ProductId;";
        await unit.Connection.ExecuteAsync(sql, new { ProductId = productId }, unit.Transaction);
    }

    public async Task<bool> HasSalesHistoryAsync(IUnitOfWork unit, int productId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT CASE WHEN EXISTS (SELECT 1 FROM SaleLine WHERE ProductId = @ProductId) THEN 1 ELSE 0 END;";
        return await unit.Connection.QuerySingleAsync<int>(sql, new { ProductId = productId }, unit.Transaction) != 0;
    }

    private static ProductDto Map(ProductRow row) => new(
        row.ProductId,
        row.Plu,
        row.Name,
        ProductUnits.Parse(row.Unit),
        row.PriceCop,
        row.StockMilli,
        row.IsActive != 0,
        row.UnitLocked != 0,
        row.HasSalesHistory != 0);

    private sealed class ProductRow
    {
        public int ProductId { get; set; }

        public string Plu { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Unit { get; set; } = string.Empty;

        public long PriceCop { get; set; }

        public long StockMilli { get; set; }

        public int IsActive { get; set; }

        public int UnitLocked { get; set; }

        public int HasSalesHistory { get; set; }
    }
}
