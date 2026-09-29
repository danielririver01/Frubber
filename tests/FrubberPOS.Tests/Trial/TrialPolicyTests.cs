using FrubberPOS.Domain.Trial;

namespace FrubberPOS.Tests.Trial;

/// <summary>
/// Proves the 30-day rule, the exact expiry boundary, and that any
/// backwards clock blocks even inside the trial window.
/// </summary>
public sealed class TrialPolicyTests
{
    private const long Day = 86_400_000L;
    private const long First = 1_790_640_000_000L;

    [Fact]
    public void Evaluate_FreshStart_GivesThirtyDays()
    {
        var assessment = TrialPolicy.Evaluate(First, First, First);

        Assert.Equal(TrialOutcome.Active, assessment.Outcome);
        Assert.Equal(30, assessment.DaysLeft);
    }

    [Fact]
    public void Evaluate_TwentyNineDaysIn_GivesOneDay()
    {
        var assessment = TrialPolicy.Evaluate(First, First + 29 * Day, First + 29 * Day);

        Assert.Equal(TrialOutcome.Active, assessment.Outcome);
        Assert.Equal(1, assessment.DaysLeft);
    }

    [Fact]
    public void Evaluate_ExactlyThirtyDays_IsExpired()
    {
        var assessment = TrialPolicy.Evaluate(First, First + 30 * Day, First + 30 * Day);

        Assert.Equal(TrialOutcome.Expired, assessment.Outcome);
        Assert.Equal(0, assessment.DaysLeft);
    }

    [Fact]
    public void Evaluate_AfterThirtyDays_IsExpired()
    {
        var assessment = TrialPolicy.Evaluate(First, First + 60 * Day, First + 60 * Day);

        Assert.Equal(TrialOutcome.Expired, assessment.Outcome);
    }

    [Fact]
    public void Evaluate_ClockBack_BlocksEvenInsideTrial()
    {
        var assessment = TrialPolicy.Evaluate(First, First + 10 * Day, First + 9 * Day);

        Assert.Equal(TrialOutcome.ClockTampered, assessment.Outcome);
    }

    [Fact]
    public void Evaluate_ClockBackBeatsExpiry()
    {
        var assessment = TrialPolicy.Evaluate(First, First + 40 * Day, First + 39 * Day);

        Assert.Equal(TrialOutcome.ClockTampered, assessment.Outcome);
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(-5, 1, 2)]
    public void Evaluate_NonPositiveInstants_Throw(long first, long lastSeen, long now)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrialPolicy.Evaluate(first, lastSeen, now));
    }
}
