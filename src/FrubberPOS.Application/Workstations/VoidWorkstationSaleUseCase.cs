using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Trial;

namespace FrubberPOS.Application.Workstations;

public interface IWorkstationSaleVoidStore
{
    Task<Result<VoidSaleResponse>> VoidSaleAsync(long saleNumber, CancellationToken cancellationToken);
}

public sealed class VoidWorkstationSaleUseCase(
    IWorkstationSaleVoidStore store, ICurrentSession session, IOperationalGate gate)
    : IUseCase<VoidSaleRequest, VoidSaleResponse>
{
    public async Task<Result<VoidSaleResponse>> ExecuteAsync(
        VoidSaleRequest request, CancellationToken cancellationToken)
    {
        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<VoidSaleResponse>.Failure(denied);
        }
        if (request.SaleNumber <= 0)
        {
            return Result<VoidSaleResponse>.Failure(new(ErrorCode.Validation, "El número de venta no es válido."));
        }

        var blocked = await gate.CheckAsync(cancellationToken);
        return blocked is null
            ? await store.VoidSaleAsync(request.SaleNumber, cancellationToken)
            : Result<VoidSaleResponse>.Failure(blocked);
    }
}
