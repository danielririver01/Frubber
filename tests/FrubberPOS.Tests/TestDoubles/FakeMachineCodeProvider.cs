using FrubberPOS.Application.Licensing;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// Fixed team fingerprint for licensing tests. Production code never uses this.
/// </summary>
public sealed class FakeMachineCodeProvider : IMachineCodeProvider
{
    private readonly MachineFingerprint fingerprint;

    public FakeMachineCodeProvider(string machineCode, params string[] redactedIdentifiers)
    {
        fingerprint = new MachineFingerprint(machineCode, redactedIdentifiers);
    }

    public Task<MachineFingerprint> GetFingerprintAsync(CancellationToken cancellationToken) =>
        Task.FromResult(fingerprint);
}
