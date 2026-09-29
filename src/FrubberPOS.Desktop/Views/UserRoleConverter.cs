using System.Globalization;
using System.Windows.Data;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Role codes to Spanish display text (P-UX §6.5 user cards).
/// </summary>
public sealed class UserRoleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is UserRole.Admin ? "Administrador" : "Cajero";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
