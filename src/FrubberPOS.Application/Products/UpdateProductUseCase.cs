using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Application.Products;

public sealed record UpdateProductRequest(int ProductId, string Plu, string Name, ProductUnit Unit, long PriceCop);

public sealed record UpdateProductResponse;

/// <summary>
/// Edits PLU, name, unit and price (JHO-32). Stock never changes here:
/// it moves only through sales and the direct adjustments of JHO-33. The
/// PLU is re-normalized and re-checked unique. The unit is immutable once
/// the product has stock or sales history (JHO-31): the refusal message
/// states why. Admin only.
/// </summary>
public sealed class UpdateProductUseCase : IUseCase<UpdateProductRequest, UpdateProductResponse>
{
    private readonly IProductRepository products;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public UpdateProductUseCase(IProductRepository products, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.products = products;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<UpdateProductResponse>> ExecuteAsync(UpdateProductRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<UpdateProductResponse>.Failure(denied);
        }

        var plu = ProductInputs.NormalizePlu(request.Plu);
        var name = ProductInputs.NormalizeName(request.Name);

        var invalid = ProductInputs.ValidatePlu(plu)
            ?? ProductInputs.ValidateName(name)
            ?? ProductInputs.ValidateUnit(request.Unit)
            ?? ProductInputs.ValidatePrice(request.PriceCop);
        if (invalid is not null)
        {
            return Result<UpdateProductResponse>.Failure(invalid);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        var current = await products.FindByIdAsync(unit, request.ProductId, cancellationToken);
        if (current is null)
        {
            return Result<UpdateProductResponse>.Failure(new Error(ErrorCode.NotFound, "El producto no existe."));
        }

        if (await products.PluExistsAsync(unit, plu, request.ProductId, cancellationToken))
        {
            return Result<UpdateProductResponse>.Failure(new Error(
                ErrorCode.Conflict, "El PLU ya está en uso.", "Plu"));
        }

        if (request.Unit != current.Unit)
        {
            if (current.StockMilli != 0)
            {
                return Result<UpdateProductResponse>.Failure(new Error(
                    ErrorCode.Conflict,
                    "No se puede cambiar la unidad porque el producto tiene existencias. Ajuste la existencia a cero primero.",
                    "Unit"));
            }

            if (await products.HasSalesHistoryAsync(unit, request.ProductId, cancellationToken))
            {
                return Result<UpdateProductResponse>.Failure(new Error(
                    ErrorCode.Conflict,
                    "No se puede cambiar la unidad porque el producto tiene ventas registradas con la unidad actual.",
                    "Unit"));
            }
        }

        await products.UpdateAsync(unit, request.ProductId, plu, name, request.Unit, request.PriceCop, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<UpdateProductResponse>.Success(new UpdateProductResponse());
    }
}
