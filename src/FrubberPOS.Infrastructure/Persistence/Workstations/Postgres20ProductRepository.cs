using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

public sealed class Postgres20ProductRepository(Postgres20WorkstationStore store) : IProductRepository
{
    private readonly Postgres20AdminCommands commands = new(store);

    private const string Select = """
        SELECT product_id AS ProductId, plu, name, unit, price_cop AS PriceCop, stock_milli AS StockMilli,
               is_active AS IsActive, unit_locked AS UnitLocked, has_sales_history AS HasSalesHistory
        FROM public.app_admin_products(@Token)
        """;

    public async Task<ProductDto?> FindByIdAsync(IUnitOfWork unit, int productId, CancellationToken ct) =>
        (await commands.QueryAsync(unit, Select + " WHERE product_id = @productId", new { productId },
            unit.Connection.QuerySingleOrDefaultAsync<Row>, ct))?.ToDto();

    public async Task<ProductDto?> FindByPluAsync(IUnitOfWork unit, string plu, CancellationToken ct) =>
        (await commands.QueryAsync(unit, Select + " WHERE plu = @plu", new { plu },
            unit.Connection.QuerySingleOrDefaultAsync<Row>, ct))?.ToDto();

    public Task<bool> PluExistsAsync(IUnitOfWork unit, string plu, int? excludeProductId, CancellationToken ct) =>
        commands.QueryAsync(unit, """
            SELECT EXISTS (SELECT FROM public.app_admin_products(@Token)
            WHERE plu = @plu AND product_id IS DISTINCT FROM @excludeProductId)
            """, new { plu, excludeProductId }, unit.Connection.QuerySingleAsync<bool>, ct);

    public Task<string> GetNextPluAsync(IUnitOfWork unit, CancellationToken ct) =>
        commands.QueryAsync(unit, """
            SELECT (coalesce(max(CASE WHEN plu ~ '^[0-9]+$' THEN plu::numeric END), 0) + 1)::text
            FROM public.app_admin_products(@Token)
            """, null, unit.Connection.QuerySingleAsync<string>, ct);

    public async Task<IReadOnlyList<ProductDto>> SearchAsync(IUnitOfWork unit, string term, ProductStatusFilter filter, CancellationToken ct)
    {
        var rows = await commands.QueryAsync(unit, Select + """
             WHERE (@Filter = 0 OR (@Filter = 1 AND is_active) OR (@Filter = 2 AND NOT is_active))
               AND (starts_with(plu, upper(@term)) OR strpos(lower(name), lower(@term)) > 0 OR @term = '')
             ORDER BY is_active DESC, name, product_id LIMIT 500
            """, new { Filter = (int)filter, term }, unit.Connection.QueryAsync<Row>, ct);
        return rows.Select(row => row.ToDto()).ToList();
    }

    public Task<int> CreateAsync(IUnitOfWork unit, string plu, string name, ProductUnit productUnit,
        long priceCop, long stockMilli, bool unitLocked, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT public.app_admin_product_create(@Token, @plu, @name, @Unit, @priceCop, @stockMilli, @unitLocked)",
            new { plu, name, Unit = ProductUnits.ToCode(productUnit), priceCop, stockMilli, unitLocked },
            unit.Connection.QuerySingleAsync<int>, ct);

    public Task UpdateAsync(IUnitOfWork unit, int productId, string plu, string name, ProductUnit productUnit,
        long priceCop, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT public.app_admin_product_update(@Token, @productId, @plu, @name, @Unit, @priceCop)",
            new { productId, plu, name, Unit = ProductUnits.ToCode(productUnit), priceCop }, unit.Connection.ExecuteAsync, ct);

    public Task SetActiveAsync(IUnitOfWork unit, int productId, bool isActive, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT public.app_admin_product_set_active(@Token, @productId, @isActive)",
            new { productId, isActive }, unit.Connection.ExecuteAsync, ct);

    public Task SetStockAsync(IUnitOfWork unit, int productId, long newStockMilli, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT public.app_admin_product_set_stock(@Token, @productId, @newStockMilli)",
            new { productId, newStockMilli }, unit.Connection.ExecuteAsync, ct);

    public Task DeleteAsync(IUnitOfWork unit, int productId, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT public.app_admin_product_delete(@Token, @productId)",
            new { productId }, unit.Connection.ExecuteAsync, ct);

    public Task<bool> HasSalesHistoryAsync(IUnitOfWork unit, int productId, CancellationToken ct) =>
        commands.QueryAsync(unit, "SELECT coalesce((SELECT has_sales_history FROM public.app_admin_products(@Token) WHERE product_id = @productId), false)",
            new { productId }, unit.Connection.QuerySingleAsync<bool>, ct);

    private sealed record Row(int ProductId, string Plu, string Name, string Unit, long PriceCop,
        long StockMilli, bool IsActive, bool UnitLocked, bool HasSalesHistory)
    {
        public ProductDto ToDto() => new(ProductId, Plu, Name, ProductUnits.Parse(Unit), PriceCop,
            StockMilli, IsActive, UnitLocked, HasSalesHistory);
    }
}
