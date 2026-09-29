using FrubberPOS.Domain.Licensing;
using FrubberPOS.Infrastructure.Licensing;

namespace FrubberPOS.Tests.Licensing;

/// <summary>
/// Proves the team fingerprint is stable, well-shaped, and redacted.
/// </summary>
public sealed class WindowsMachineCodeProviderTests
{
    [Fact]
    public async Task GetFingerprint_ReturnsStableWellShapedCode()
    {
        var provider = new WindowsMachineCodeProvider();

        var first = await provider.GetFingerprintAsync(CancellationToken.None);
        var second = await provider.GetFingerprintAsync(CancellationToken.None);

        Assert.Equal(first.MachineCode, second.MachineCode);
        Assert.Matches("^[0-9A-F]{16}$", first.MachineCode);
        Assert.Equal(MachineCode.FormatDisplay(first.MachineCode).Replace("-", string.Empty), first.MachineCode);
        Assert.NotEmpty(first.RedactedIdentifiers);
    }
}
