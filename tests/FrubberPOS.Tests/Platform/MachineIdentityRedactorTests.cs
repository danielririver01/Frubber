using FrubberPOS.Application.System;

namespace FrubberPOS.Tests.Platform;

public sealed class MachineIdentityRedactorTests
{
    [Fact]
    public void Redact_IsStable()
    {
        const string serial = "WD-WX71A12BC345";

        Assert.Equal(MachineIdentityRedactor.Redact(serial), MachineIdentityRedactor.Redact(serial));
    }

    [Fact]
    public void Redact_NeverExposesFullSerial()
    {
        const string serial = "WD-WX71A12BC345";

        var redacted = MachineIdentityRedactor.Redact(serial);

        Assert.NotEqual(serial, redacted);
        Assert.DoesNotContain(serial, redacted);
    }

    [Fact]
    public void Redact_DistinguishesDifferentSerials()
    {
        Assert.NotEqual(
            MachineIdentityRedactor.Redact("SERIAL-ONE-123"),
            MachineIdentityRedactor.Redact("SERIAL-TWO-456"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Redact_RejectsEmpty(string? serial)
    {
        Assert.Throws<ArgumentException>(() => MachineIdentityRedactor.Redact(serial!));
    }
}
