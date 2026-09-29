using FrubberPOS.Infrastructure.System;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Platform;

public sealed class ClockTests
{
    [Fact]
    public void FakeClock_ReturnsConfiguredInstant()
    {
        var instant = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(instant);

        Assert.Equal(instant, clock.UtcNow);
        Assert.Equal(instant.ToUnixTimeMilliseconds(), clock.UtcNow.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void FakeClock_IsSettable()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);

        clock.UtcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(-300));

        Assert.Equal(-300, clock.UtcNow.Offset.TotalMinutes);
    }

    [Fact]
    public void SystemClock_ReturnsNearUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var actual = SystemClock.Instance.UtcNow;

        Assert.InRange(actual, before.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }
}
