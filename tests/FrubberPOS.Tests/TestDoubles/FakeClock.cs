using FrubberPOS.Application.System;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// Settable clock for tests (JHO-22). Production code never uses this.
/// </summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset initial)
    {
        UtcNow = initial;
    }

    public DateTimeOffset UtcNow { get; set; }
}
