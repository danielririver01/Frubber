using FrubberPOS.Application.Common;
using FrubberPOS.Domain.Trial;

namespace FrubberPOS.Application.Trial;

/// <summary>
/// Operational gate port (P-DD §11.3, RN-LIC-002/010): transactional use
/// cases re-ask the licensing service before opening or closing the cash
/// session and before completing or voiding a sale. <c>null</c> means the
/// operation may proceed.
/// </summary>
public interface IOperationalGate
{
    Task<Error?> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Default gate (JHO-57): delegates the decision to
/// <see cref="EvaluateTrialUseCase"/> and maps a blocked outcome to
/// <see cref="ErrorCode.LicenseBlocked"/> with a user-safe message,
/// without touching any business data (RN-LIC-003).
/// </summary>
public sealed class OperationalGate : IOperationalGate
{
    private readonly EvaluateTrialUseCase evaluate;

    public OperationalGate(EvaluateTrialUseCase evaluate)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        this.evaluate = evaluate;
    }

    public async Task<Error?> CheckAsync(CancellationToken cancellationToken)
    {
        var result = await evaluate.ExecuteAsync(new EvaluateTrialRequest(), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        return result.Value.Outcome switch
        {
            _ when result.Value.IsLicensed => null,
            TrialOutcome.Active => null,
            TrialOutcome.ClockTampered => new Error(
                ErrorCode.LicenseBlocked,
                "Se detectó un retroceso en el reloj del equipo. Corrija la fecha y hora para continuar."),
            _ => new Error(
                ErrorCode.LicenseBlocked,
                "El periodo de prueba terminó. Active la licencia para continuar."),
        };
    }
}
