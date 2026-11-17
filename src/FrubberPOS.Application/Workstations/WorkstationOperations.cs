using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Security;
using FrubberPOS.Application.Trial;
using FrubberPOS.Application.Users;

namespace FrubberPOS.Application.Workstations;

public sealed record WorkstationInfo(long Id, string Name);

public sealed record WorkstationSale(long SaleId, long SaleNumber, long TotalCop);

public sealed record WorkstationCashState(long? CashId);

public sealed record CloseWorkstationCashRequest(long CashId);

public sealed record WorkstationCashClosure(
    long CashId, long OpeningBalance, long CashSales, long TransferSales,
    long DiscountTotal, long NetSales, long ExpectedCash, int SaleCount);

public sealed record CheckWorkstationStartupRequest;

public interface IWorkstationStore
{
    Task<Result<WorkstationInfo>> CheckAsync(CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ActiveUserDto>>> ListUsersAsync(CancellationToken cancellationToken);
    Task<Result<SignedInUser>> AuthenticateAsync(int userId, string pin, CancellationToken cancellationToken);
    Task<Result<WorkstationCashState>> GetCashAsync(CancellationToken cancellationToken);
    Task<Result<long>> OpenCashAsync(int userId, long balanceCop, CancellationToken cancellationToken);
    Task<Result<WorkstationCashClosure>> CloseCashAsync(long cashId, int userId, CancellationToken cancellationToken);
    Task<Result<ProductDto>> FindProductAsync(string plu, CancellationToken cancellationToken);
    Task<Result<WorkstationSale>> RegisterSaleAsync(
        int userId, Guid requestId, FinalizeSaleRequest request, CancellationToken cancellationToken);
}

public sealed class CheckWorkstationStartupUseCase(IWorkstationStore store, IOperationalGate gate)
    : IUseCase<CheckWorkstationStartupRequest, WorkstationInfo>
{
    public async Task<Result<WorkstationInfo>> ExecuteAsync(
        CheckWorkstationStartupRequest request, CancellationToken cancellationToken)
    {
        var terminal = await store.CheckAsync(cancellationToken);
        if (terminal.IsFailure)
        {
            return terminal;
        }

        var blocked = await gate.CheckAsync(cancellationToken);
        return blocked is null ? terminal : Result<WorkstationInfo>.Failure(blocked);
    }
}

public sealed class AuthenticateWorkstationUserUseCase(IWorkstationStore store, ICurrentSession session)
    : IUseCase<AuthenticateUserRequest, SignedInUser>
{
    public async Task<Result<SignedInUser>> ExecuteAsync(
        AuthenticateUserRequest request, CancellationToken cancellationToken)
    {
        if (!PinRules.IsValidFormat(request.Pin))
        {
            return Result<SignedInUser>.Failure(new Error(ErrorCode.Validation, "Ingrese un PIN de 4 dígitos."));
        }

        var authenticated = await store.AuthenticateAsync(request.UserId, request.Pin, cancellationToken);
        if (authenticated.IsSuccess)
        {
            session.SignIn(authenticated.Value);
        }
        return authenticated;
    }
}

public sealed class OpenWorkstationCashUseCase(IWorkstationStore store, ICurrentSession session, IOperationalGate gate)
    : IUseCase<OpenCashSessionRequest, long>
{
    public async Task<Result<long>> ExecuteAsync(OpenCashSessionRequest request, CancellationToken cancellationToken)
    {
        var user = session.Current;
        if (user is null)
        {
            return Result<long>.Failure(new Error(ErrorCode.Authorization, "Debe iniciar sesión."));
        }
        if (request.OpeningBalanceCop < 0)
        {
            return Result<long>.Failure(new Error(ErrorCode.Validation, "El saldo inicial no puede ser negativo."));
        }

        var blocked = await gate.CheckAsync(cancellationToken);
        return blocked is null
            ? await store.OpenCashAsync(user.UserId, request.OpeningBalanceCop, cancellationToken)
            : Result<long>.Failure(blocked);
    }
}

public sealed class CloseWorkstationCashUseCase(IWorkstationStore store, ICurrentSession session, IOperationalGate gate)
    : IUseCase<CloseWorkstationCashRequest, WorkstationCashClosure>
{
    public async Task<Result<WorkstationCashClosure>> ExecuteAsync(
        CloseWorkstationCashRequest request, CancellationToken cancellationToken)
    {
        var user = session.Current;
        if (user is null)
        {
            return Result<WorkstationCashClosure>.Failure(new Error(ErrorCode.Authorization, "Debe iniciar sesión."));
        }

        var blocked = await gate.CheckAsync(cancellationToken);
        return blocked is null
            ? await store.CloseCashAsync(request.CashId, user.UserId, cancellationToken)
            : Result<WorkstationCashClosure>.Failure(blocked);
    }
}

public sealed class FinalizeWorkstationSaleUseCase(IWorkstationStore store, ICurrentSession session, IOperationalGate gate)
    : IUseCase<FinalizeSaleRequest, WorkstationSale>
{
    public async Task<Result<WorkstationSale>> ExecuteAsync(
        FinalizeSaleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = session.Current;
        if (user is null)
        {
            return Result<WorkstationSale>.Failure(new Error(ErrorCode.Authorization, "Debe iniciar sesión."));
        }
        if (request.Lines is not { Count: > 0 })
        {
            return Result<WorkstationSale>.Failure(new Error(ErrorCode.Validation, "La venta no puede estar vacía."));
        }

        var blocked = await gate.CheckAsync(cancellationToken);
        return blocked is null
            ? await store.RegisterSaleAsync(user.UserId, Guid.NewGuid(), request, cancellationToken)
            : Result<WorkstationSale>.Failure(blocked);
    }
}
