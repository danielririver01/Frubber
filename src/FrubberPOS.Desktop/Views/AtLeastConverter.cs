using System.Globalization;
using System.Windows.Data;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// True cuando el entero ligado es ≥ <c>ConverterParameter</c>
/// (dots acumulativos del PIN del login: el dot N se llena con ≥N dígitos).
/// </summary>
public sealed class AtLeastConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var current = value is int i ? i : 0;
        var threshold = parameter is null ? 0 : int.Parse((string)parameter, CultureInfo.InvariantCulture);
        return current >= threshold;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
