using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;

namespace FrubberPOS.Application.Workstations;

public sealed record GetDailySalesRequest;
public sealed record WorkstationDailySales(long TerminalId, string TerminalName, long TotalCop);
public sealed record DailySalesReport(DateOnly BusinessDate, IReadOnlyList<WorkstationDailySales> Workstations, long TotalCop);

public interface IWorkstationDailySalesReader
{
    Task<Result<DailySalesReport>> ReadDailySalesAsync(CancellationToken cancellationToken);
}

public sealed class GetDailySalesUseCase(IWorkstationDailySalesReader reader, ICurrentSession session)
    : IUseCase<GetDailySalesRequest, DailySalesReport>
{
    public async Task<Result<DailySalesReport>> ExecuteAsync(GetDailySalesRequest request, CancellationToken cancellationToken)
    {
        var denied = Authorization.RequireAdmin(session);
        return denied is null
            ? await reader.ReadDailySalesAsync(cancellationToken)
            : Result<DailySalesReport>.Failure(denied);
    }
}
