using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FrubberPOS.Desktop.Views;

/// <summary>Inverse boolean → visibility for "hide while editing" blocks.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}
