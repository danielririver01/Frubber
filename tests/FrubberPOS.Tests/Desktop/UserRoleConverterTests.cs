using System.Globalization;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Tests.Desktop;

public sealed class UserRoleConverterTests
{
    [Fact]
    public void ConvertsRolesToSpanish()
    {
        var converter = new UserRoleConverter();

        Assert.Equal("Administrador", converter.Convert(UserRole.Admin, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("Cajero", converter.Convert(UserRole.Cashier, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
