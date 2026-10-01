using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Sales;

public sealed record GetSaleDetailRequest(long SaleNumber);

/// <summary>
/// Historical sale detail (JHO-48, RN-HIS-001/002/003): administrator-only
/// read composed strictly from the snapshots stored at sale time (and the
/// void row for anuladas). Nothing is recomputed from the current catalog,
/// so editing a product never rewrites history.
/// </summary>
public sealed class GetSaleDetailUseCase : IUseCase<GetSaleDetailRequest, SaleDetailDto>
{
    private readonly ISaleHistoryRepository history;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public GetSaleDetailUseCase(
        ISaleHistoryRepository history,
        IUnitOfWorkFactory units,
        ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.history = history;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<SaleDetailDto>> ExecuteAsync(GetSaleDetailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<SaleDetailDto>.Failure(denied);
        }

        if (request.SaleNumber <= 0)
        {
            return Result<SaleDetailDto>.Failure(new Error(ErrorCode.Validation, "El número de venta no es válido."));
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var detail = await history.FindDetailAsync(unit, request.SaleNumber, cancellationToken);
        return detail is null
            ? Result<SaleDetailDto>.Failure(new Error(ErrorCode.NotFound, $"No se encontró la venta V-{request.SaleNumber:D6}."))
            : Result<SaleDetailDto>.Success(detail);
    }
}
