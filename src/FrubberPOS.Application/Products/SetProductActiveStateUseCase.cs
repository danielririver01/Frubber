using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Products;

public sealed record SetProductActiveStateRequest(int ProductId, bool IsActive);

public sealed record SetProductActiveStateResponse;

/// <summary>
/// Activates or deactivates a product (JHO-32). Deactivation hides it from
/// the TPV without losing history; reactivation brings it back. Admin only.
/// </summary>
public sealed class SetProductActiveStateUseCase : IUseCase<SetProductActiveStateRequest, SetProductActiveStateResponse>
{
    private readonly IProductRepository products;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public SetProductActiveStateUseCase(IProductRepository products, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.products = products;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<SetProductActiveStateResponse>> ExecuteAsync(SetProductActiveStateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<SetProductActiveStateResponse>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        if (await products.FindByIdAsync(unit, request.ProductId, cancellationToken) is null)
        {
            return Result<SetProductActiveStateResponse>.Failure(new Error(ErrorCode.NotFound, "El producto no existe."));
        }

        await products.SetActiveAsync(unit, request.ProductId, request.IsActive, cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<SetProductActiveStateResponse>.Success(new SetProductActiveStateResponse());
    }
}
