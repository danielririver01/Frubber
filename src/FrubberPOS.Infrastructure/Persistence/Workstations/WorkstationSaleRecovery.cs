using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Trial;
using FrubberPOS.Application.Workstations;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

[SupportedOSPlatform("windows")]
internal sealed class WorkstationSaleRecovery(
    ProtectedSaleAttemptJournal journal,
    Guid databaseId,
    long terminalId,
    ICurrentSession session,
    IOperationalGate gate,
    Func<SaleAttempt, CancellationToken, Task<Result<WorkstationSale>>> send) : IWorkstationSaleRecovery
{
    private Guid displayedRequest;

    public bool HasPendingAttempt
    {
        get
        {
            try
            {
                return journal.Read() is not null;
            }
            catch (Exception ex) when (IsJournalFailure(ex))
            {
                return true;
            }
        }
    }

    public Task<Result<WorkstationSale>> RegisterAsync(long cashId, FinalizeSaleRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(cashId, request, cancellationToken);

    public Task<Result<WorkstationSale>> RecoverAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(null, null, cancellationToken);

    public void Acknowledge()
    {
        if (displayedRequest != Guid.Empty)
        {
            journal.Acknowledge(displayedRequest);
            displayedRequest = Guid.Empty;
        }
    }

    private async Task<Result<WorkstationSale>> ExecuteAsync(long? cashId, FinalizeSaleRequest? request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } user)
        {
            return Result<WorkstationSale>.Failure(new Error(ErrorCode.Authorization, "Debe iniciar sesión."));
        }

        var blocked = await gate.CheckAsync(cancellationToken);
        if (blocked is not null)
        {
            return Result<WorkstationSale>.Failure(blocked);
        }

        try
        {
            var entry = journal.Read();
            if (request is not null)
            {
                if (entry is not null)
                {
                    return Result<WorkstationSale>.Failure(new Error(ErrorCode.Conflict, "Primero recupere el cobro pendiente."));
                }
                if (cashId is not > 0 || request.Lines is not { Count: > 0 })
                {
                    return Result<WorkstationSale>.Failure(new Error(ErrorCode.Validation, "Abra una caja y agregue productos."));
                }

                var attempt = new SaleAttempt(Guid.NewGuid(), databaseId, terminalId, cashId.Value, user.UserId, request);
                journal.Start(attempt);
                entry = journal.Read();
            }

            if (entry is null)
            {
                return Result<WorkstationSale>.Failure(new Error(ErrorCode.NotFound, "No hay un cobro pendiente."));
            }
            if (entry.Attempt.DatabaseId != databaseId || entry.Attempt.TerminalId != terminalId || entry.Attempt.CashierId != user.UserId)
            {
                return Result<WorkstationSale>.Failure(new Error(ErrorCode.Authorization, "El cobro requiere el puesto y el cajero originales."));
            }
            if (entry.Receipt is { } receipt)
            {
                displayedRequest = entry.Attempt.RequestId;
                return Result<WorkstationSale>.Success(receipt);
            }

            var result = await send(entry.Attempt, cancellationToken);
            if (result.IsSuccess)
            {
                journal.Confirm(entry.Attempt.RequestId, result.Value);
                displayedRequest = entry.Attempt.RequestId;
            }
            return result;
        }
        catch (Exception ex) when (IsJournalFailure(ex))
        {
            return Result<WorkstationSale>.Failure(new Error(ErrorCode.Storage, "No se pudo leer o guardar el cobro pendiente."));
        }
    }

    private static bool IsJournalFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException;
}
