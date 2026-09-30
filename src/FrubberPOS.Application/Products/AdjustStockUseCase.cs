using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Application.Products;

public sealed record AdjustStockRequest(int ProductId, long NewStockMilli);

public sealed record AdjustStockResponse(long StockMilli);

/// <summary>
/// Direct stock replacement (RN-INV-003, JHO-33): admin only, no reason
/// asked and no auditable adjustment history (RN-INV-004) — the approved
/// technical movement is this single UPDATE. Negative values are allowed
/// (RN-INV-002 says stock may end negative); unit precision rules apply
/// (RN-INV-006: whole pieces for UNIT, thousandths for KG/LB). The update
/// and the unit-lock flag travel in one statement inside the caller's
/// transaction.
/// </summary>
public sealed class AdjustStockUseCase : IUseCase<AdjustStockRequest, AdjustStockResponse>
{
    private readonly IProductRepository products;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public AdjustStockUseCase(IProductRepository products, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.products = products;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<AdjustStockResponse>> ExecuteAsync(AdjustStockRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<AdjustStockResponse>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        var product = await products.FindByIdAsync(unit, request.ProductId, cancellationToken);
        if (product is null)
        {
            return Result<AdjustStockResponse>.Failure(new Error(ErrorCode.NotFound, "El producto no existe."));
        }

        if (product.Unit == ProductUnit.Unit && !ProductQuantities.IsWholeUnits(request.NewStockMilli))
        {
            return Result<AdjustStockResponse>.Failure(new Error(
                ErrorCode.Validation, "La existencia de un producto por unidad debe ser un número entero.", "NewStockMilli"));
        }

        await products.SetStockAsync(unit, request.ProductId, request.NewStockMilli, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<AdjustStockResponse>.Success(new AdjustStockResponse(request.NewStockMilli));
    }
}
