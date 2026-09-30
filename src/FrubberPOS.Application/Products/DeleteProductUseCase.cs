using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Products;

public sealed record DeleteProductRequest(int ProductId);

public sealed record DeleteProductResponse;

/// <summary>
/// Permanent deletion only when the product has no sale lines (JHO-32);
/// otherwise <see cref="ErrorCode.Conflict"/> directing to deactivate
/// instead. SaleLine's FK is ON DELETE RESTRICT, so the friendly check
/// runs first inside the same transaction. The remaining conditions of a
/// conditioned deletion (e.g. non-zero stock) belong to JHO-31.
/// Admin only.
/// </summary>
public sealed class DeleteProductUseCase : IUseCase<DeleteProductRequest, DeleteProductResponse>
{
    private readonly IProductRepository products;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public DeleteProductUseCase(IProductRepository products, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.products = products;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<DeleteProductResponse>> ExecuteAsync(DeleteProductRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<DeleteProductResponse>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        if (await products.FindByIdAsync(unit, request.ProductId, cancellationToken) is null)
        {
            return Result<DeleteProductResponse>.Failure(new Error(ErrorCode.NotFound, "El producto no existe."));
        }

        if (await products.HasSalesHistoryAsync(unit, request.ProductId, cancellationToken))
        {
            return Result<DeleteProductResponse>.Failure(new Error(
                ErrorCode.Conflict, "El producto tiene ventas y no puede eliminarse. Desactívelo en su lugar."));
        }

        await products.DeleteAsync(unit, request.ProductId, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<DeleteProductResponse>.Success(new DeleteProductResponse());
    }
}
