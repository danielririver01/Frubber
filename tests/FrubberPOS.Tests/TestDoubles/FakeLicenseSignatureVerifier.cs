using FrubberPOS.Application.Licensing;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// Scriptable signature check for licensing tests. Production code never uses this.
/// </summary>
public sealed class FakeLicenseSignatureVerifier : ILicenseSignatureVerifier
{
    private readonly Func<string, string, DateOnly, byte[], bool> rule;

    public FakeLicenseSignatureVerifier(Func<string, string, DateOnly, byte[], bool> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        this.rule = rule;
    }

    public bool Verify(string keyId, string machineCode, DateOnly issuedDate, byte[] signature) =>
        rule(keyId, machineCode, issuedDate, signature);
}
