using FrubberPOS.Application.Security;

namespace FrubberPOS.Tests.Security;

public sealed class PinRulesTests
{
    [Theory]
    [InlineData("0000")]
    [InlineData("1234")]
    [InlineData("9999")]
    public void FourDigits_AreValid(string pin)
    {
        Assert.True(PinRules.IsValidFormat(pin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("12 4")]
    public void OtherShapes_AreInvalid(string? pin)
    {
        Assert.False(PinRules.IsValidFormat(pin));
    }
}
