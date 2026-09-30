using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Application.Products;

public sealed record CreateProductRequest(string Plu, string Name, ProductUnit Unit, long PriceCop, long StockMilli);

public sealed record CreateProductResponse(int ProductId);

/// <summary>
/// Creates an active product (JHO-32). The PLU is normalized (trim +
/// uppercase) per the approved rule and must be unique; money and stock
/// stay whole-COP and thousandths (ADR-009). Initial stock marks the unit
/// as locked for storage parity (001_Initial.sql); the lock transitions
/// themselves belong to JHO-31. Admin only.
/// </summary>
public sealed class CreateProductUseCase : IUseCase<CreateProductRequest, CreateProductResponse>
{
    private readonly IProductRepository products;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public CreateProductUseCase(IProductRepository products, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.products = products;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<CreateProductResponse>> ExecuteAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<CreateProductResponse>.Failure(denied);
        }

        var plu = ProductInputs.NormalizePlu(request.Plu);
        var name = ProductInputs.NormalizeName(request.Name);

        var invalid = ProductInputs.ValidatePlu(plu)
            ?? ProductInputs.ValidateName(name)
            ?? ProductInputs.ValidateUnit(request.Unit)
            ?? ProductInputs.ValidatePrice(request.PriceCop)
            ?? ValidateInitialStock(request.Unit, request.StockMilli);
        if (invalid is not null)
        {
            return Result<CreateProductResponse>.Failure(invalid);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        if (await products.PluExistsAsync(unit, plu, null, cancellationToken))
        {
            return Result<CreateProductResponse>.Failure(new Error(
                ErrorCode.Conflict, "El PLU ya está en uso.", "Plu"));
        }

        var id = await products.CreateAsync(
            unit, plu, name, request.Unit, request.PriceCop, request.StockMilli,
            unitLocked: request.StockMilli != 0, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<CreateProductResponse>.Success(new CreateProductResponse(id));
    }

    private static Error? ValidateInitialStock(ProductUnit unit, long stockMilli)
    {
        if (stockMilli < 0)
        {
            return new Error(ErrorCode.Validation, "La existencia inicial no puede ser negativa.", "StockMilli");
        }

        if (!ProductQuantities.IsWholeUnits(stockMilli) && unit == ProductUnit.Unit && stockMilli != 0)
        {
            return new Error(ErrorCode.Validation, "Las existencias de productos por unidad deben ser enteras.", "StockMilli");
        }

        return null;
    }
}
