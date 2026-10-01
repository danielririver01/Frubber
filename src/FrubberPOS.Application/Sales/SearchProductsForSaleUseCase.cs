using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;

namespace FrubberPOS.Application.Sales;

public sealed record SearchForSaleRequest(string Query);

public sealed record SearchForSaleResponse(IReadOnlyList<TpvSearchItemDto> Items);

/// <summary>
/// TPV search row (JHO-40): PLU, name, unit, price and stock only.
/// </summary>
public sealed record TpvSearchItemDto(
    int ProductId,
    string Plu,
    string Name,
    string UnitCode,
    long PriceCop,
    long StockMilli);

/// <summary>
/// Compact TPV search by PLU or name (JHO-40, RN-VEN-003, P-UX-006):
/// active products only, exact PLU match ranks first, then PLU prefix,
/// then name substring.
/// </summary>
public sealed class SearchProductsForSaleUseCase : IUseCase<SearchForSaleRequest, SearchForSaleResponse>
{
    private readonly IProductRepository products;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public SearchProductsForSaleUseCase(IProductRepository products, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.products = products;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<SearchForSaleResponse>> ExecuteAsync(SearchForSaleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireSignedIn(session);
        if (denied is not null)
        {
            return Result<SearchForSaleResponse>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var found = await products.SearchAsync(
            unit, request.Query?.Trim() ?? string.Empty, ProductStatusFilter.Activos, cancellationToken);

        var exactPlu = request.Query?.Trim().ToUpperInvariant() ?? string.Empty;
        var items = found
            .OrderBy(p => p.Plu == exactPlu ? 0 : 1)
            .ThenBy(p => p.Plu.StartsWith(exactPlu, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new TpvSearchItemDto(
                p.ProductId, p.Plu, p.Name,
                Domain.Products.ProductUnits.ToCode(p.Unit), p.PriceCop, p.StockMilli))
            .ToList();

        return Result<SearchForSaleResponse>.Success(new SearchForSaleResponse(items));
    }
}
