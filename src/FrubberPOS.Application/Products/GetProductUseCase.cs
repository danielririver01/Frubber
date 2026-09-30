using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Products;

public sealed record GetProductRequest(int ProductId);

public sealed record GetProductResponse(ProductDto Product);

/// <summary>
/// Product detail for administration and TPV (JHO-32). Any signed-in user
/// may read; mutation is admin-only.
/// </summary>
public sealed class GetProductUseCase : IUseCase<GetProductRequest, GetProductResponse>
{
    private readonly IProductRepository products;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public GetProductUseCase(IProductRepository products, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.products = products;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<GetProductResponse>> ExecuteAsync(GetProductRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireSignedIn(session);
        if (denied is not null)
        {
            return Result<GetProductResponse>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var product = await products.FindByIdAsync(unit, request.ProductId, cancellationToken);

        if (product is null)
        {
            return Result<GetProductResponse>.Failure(new Error(ErrorCode.NotFound, "El producto no existe."));
        }

        return Result<GetProductResponse>.Success(new GetProductResponse(product));
    }
}
