using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Products;

public sealed record SuggestNextPluRequest;

public sealed record SuggestNextPluResponse(string Plu);

/// <summary>
/// Suggests the next free numeric PLU when the product editor opens
/// (JHO-87, approved by the owner 2026-10-14): MAX(numeric PLU)+1, starting
/// at 1, so the operator accepts it without thinking; a special PLU (scale
/// key) can still be typed over it. Alphanumeric PLUs count as zero and
/// never raise the suggestion. This is a convenience only — uniqueness
/// stays enforced by CreateProduct/UpdateProduct at save time. Admin only.
/// </summary>
public sealed class SuggestNextPluUseCase : IUseCase<SuggestNextPluRequest, SuggestNextPluResponse>
{
    private readonly IProductRepository products;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public SuggestNextPluUseCase(IProductRepository products, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.products = products;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<SuggestNextPluResponse>> ExecuteAsync(
        SuggestNextPluRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<SuggestNextPluResponse>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var next = await products.GetNextPluAsync(unit, cancellationToken);
        return Result<SuggestNextPluResponse>.Success(new SuggestNextPluResponse(next));
    }
}
