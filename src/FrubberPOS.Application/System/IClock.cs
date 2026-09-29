namespace FrubberPOS.Application.System;

/// <summary>
/// Current instant abstraction (P-DD §12). Production uses
/// <see cref="FrubberPOS.Infrastructure.System.SystemClock"/>; tests use a
/// fake. Domain and use cases never call <c>DateTime.Now</c> directly:
/// historical facts capture Unix UTC milliseconds plus the local offset in
/// minutes from a single instant read.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
