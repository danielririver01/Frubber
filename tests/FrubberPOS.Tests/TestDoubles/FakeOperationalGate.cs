using FrubberPOS.Application.Common;
using FrubberPOS.Application.Trial;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// Test stand-in for the licensing/clock gate (JHO-57, P-DD §11.3):
/// allows everything unless <see cref="Blocked"/> carries an error.
/// </summary>
public sealed class FakeOperationalGate : IOperationalGate
{
    /// <summary>When set, every check returns this error (a blocking state).</summary>
    public Error? Blocked { get; set; }

    public int CheckCount { get; private set; }

    public Task<Error?> CheckAsync(CancellationToken cancellationToken)
    {
        CheckCount++;
        return Task.FromResult(Blocked);
    }
}
