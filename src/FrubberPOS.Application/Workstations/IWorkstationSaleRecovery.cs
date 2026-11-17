using FrubberPOS.Application.Common;
using FrubberPOS.Application.Sales;

namespace FrubberPOS.Application.Workstations;

public interface IWorkstationSaleRecovery
{
    bool HasPendingAttempt { get; }

    Task<Result<WorkstationSale>> RegisterAsync(long cashId, FinalizeSaleRequest request, CancellationToken cancellationToken);
    Task<Result<WorkstationSale>> RecoverAsync(CancellationToken cancellationToken);
    void Acknowledge();
}
