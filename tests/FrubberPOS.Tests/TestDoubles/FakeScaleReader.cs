using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Scale;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// Programmable scale reader for tests (JHO-86): no COM port involved.
/// Production code always uses <c>SerialPortScaleReader</c>.
/// </summary>
public sealed class FakeScaleReader : IScaleReader
{
    public Result<ScaleReading> NextReading { get; set; } =
        Result<ScaleReading>.Success(new ScaleReading(1_250, true));

    public IReadOnlyList<string> Ports { get; set; } = ["COM3"];

    public ScaleSnapshot? LastSettings { get; private set; }

    public int ReadCount { get; private set; }

    public Task<Result<ScaleReading>> ReadAsync(ScaleSnapshot settings, CancellationToken cancellationToken)
    {
        LastSettings = settings;
        ReadCount++;
        return Task.FromResult(NextReading);
    }

    public Task<IReadOnlyList<string>> ListPortsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Ports);
}
