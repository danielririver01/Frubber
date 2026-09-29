using FrubberPOS.Application.System;

namespace FrubberPOS.Infrastructure.System;

/// <summary>
/// Production clock: local machine time (JHO-22). Tamper-evidence of that
/// time (trial/license) belongs to JHO-25/JHO-27, not here.
/// </summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    private SystemClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
