namespace FrubberPOS.Domain.Trial;

/// <summary>
/// Trial gate outcome (JHO-27). Licensed teams skip the trial entirely;
/// everyone else is active, expired, or clock-tampered.
/// </summary>
public enum TrialOutcome
{
    Active,
    Expired,
    ClockTampered
}

/// <summary>
/// Pure assessment of the trial state for one instant.
/// </summary>
public sealed record TrialAssessment(TrialOutcome Outcome, int DaysLeft);

/// <summary>
/// Offline 30-day trial rule (JHO-27): 30 days from the first recorded run.
/// Any instant before the last observed one means the clock went backwards
/// and blocks. Pure logic over Unix UTC milliseconds; storage and the
/// license bypass live in Application.
/// </summary>
public static class TrialPolicy
{
    public const int TrialDays = 30;

    public const long DayMilliseconds = 86_400_000L;

    public static TrialAssessment Evaluate(long firstRunUtcMs, long lastSeenUtcMs, long nowUtcMs)
    {
        if (firstRunUtcMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstRunUtcMs), "La primera ejecución debe ser positiva.");
        }

        if (lastSeenUtcMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastSeenUtcMs), "El último instante debe ser positivo.");
        }

        if (nowUtcMs < lastSeenUtcMs)
        {
            return new TrialAssessment(TrialOutcome.ClockTampered, 0);
        }

        var elapsed = nowUtcMs - firstRunUtcMs;
        if (elapsed >= TrialDays * DayMilliseconds)
        {
            return new TrialAssessment(TrialOutcome.Expired, 0);
        }

        var daysLeft = TrialDays - (int)(elapsed / DayMilliseconds);
        return new TrialAssessment(TrialOutcome.Active, Math.Max(1, daysLeft));
    }
}
