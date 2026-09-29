using FrubberPOS.Application.Common;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.System;
using FrubberPOS.Domain.Trial;

namespace FrubberPOS.Application.Trial;

public sealed record EvaluateTrialRequest;

public sealed record EvaluateTrialResponse(bool IsLicensed, TrialOutcome Outcome, int DaysLeft)
{
    public bool IsBlocked => !IsLicensed && Outcome != TrialOutcome.Active;
}

/// <summary>
/// Startup trial gate (JHO-27). Licensed teams pass untouched. The first
/// run records its instant and starts the 30 days; every later run advances
/// the last observed instant. A backwards clock or an exhausted trial
/// blocks without touching any other data.
/// </summary>
public sealed class EvaluateTrialUseCase : IUseCase<EvaluateTrialRequest, EvaluateTrialResponse>
{
    private readonly GetLicenseStatusUseCase licenseStatus;
    private readonly ITrialRepository trials;
    private readonly IUnitOfWorkFactory units;
    private readonly IClock clock;

    public EvaluateTrialUseCase(
        GetLicenseStatusUseCase licenseStatus,
        ITrialRepository trials,
        IUnitOfWorkFactory units,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(licenseStatus);
        ArgumentNullException.ThrowIfNull(trials);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(clock);
        this.licenseStatus = licenseStatus;
        this.trials = trials;
        this.units = units;
        this.clock = clock;
    }

    public async Task<Result<EvaluateTrialResponse>> ExecuteAsync(EvaluateTrialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow.ToUnixTimeMilliseconds();

        await using var unit = await units.CreateAsync(UnitOfWorkMode.Write, cancellationToken);
        var stored = await trials.ReadAsync(unit, cancellationToken);
        stored ??= new StoredTrial(now, now);

        var licensed = await licenseStatus.ExecuteAsync(new GetLicenseStatusRequest(), cancellationToken);
        if (licensed.IsSuccess && licensed.Value.IsLicensed)
        {
            await trials.SaveAsync(unit, stored with { LastSeenUtcMs = Math.Max(stored.LastSeenUtcMs, now) }, cancellationToken);
            await unit.CommitAsync(cancellationToken);
            return Result<EvaluateTrialResponse>.Success(new EvaluateTrialResponse(true, TrialOutcome.Active, 0));
        }

        var assessment = TrialPolicy.Evaluate(stored.FirstRunUtcMs, stored.LastSeenUtcMs, now);
        if (assessment.Outcome != TrialOutcome.ClockTampered)
        {
            await trials.SaveAsync(unit, stored with { LastSeenUtcMs = now }, cancellationToken);
        }

        await unit.CommitAsync(cancellationToken);
        return Result<EvaluateTrialResponse>.Success(
            new EvaluateTrialResponse(false, assessment.Outcome, assessment.DaysLeft));
    }
}
