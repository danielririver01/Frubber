using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Products;

public sealed record SearchProductsRequest(string Query, bool IncludeInactive);

public sealed record SearchProductsResponse(IReadOnlyList<ProductDto> Products);

/// <summary>
/// Product search by PLU prefix or name substring, case-insensitive
/// (JHO-32). An empty query lists the catalog (active only unless
/// <c>IncludeInactive</c>). Any signed-in user may search: the TPV needs
/// it (JHO-40). Admin-only mutations stay in the other use cases.
/// </summary>
public sealed class SearchProductsUseCase : IUseCase<SearchProductsRequest, SearchProductsResponse>
{
    private readonly IProductRepository products;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public SearchProductsUseCase(IProductRepository products, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.products = products;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<SearchProductsResponse>> ExecuteAsync(SearchProductsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireSignedIn(session);
        if (denied is not null)
        {
            return Result<SearchProductsResponse>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var found = await products.SearchAsync(unit, request.Query?.Trim() ?? string.Empty, request.IncludeInactive, cancellationToken);
        return Result<SearchProductsResponse>.Success(new SearchProductsResponse(found));
    }
}
