using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FrubberPOS.Installer;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Visible cuando el paso actual coincide con el parámetro ("1".."5").</summary>
public sealed class StepEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int step && int.TryParse(parameter as string, out var wanted) && step == wanted
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Fondo del paso del rail: activo = teal suave; completado = teal oscuro; normal = transparente.</summary>
public sealed class StepStateBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2 || values[0] is not int current || values[1] is not int step)
        {
            return Brushes.Transparent;
        }

        if (step == current) return new SolidColorBrush(Color.FromRgb(0x1B, 0x4A, 0x4A)); // activo
        return step < current ? new SolidColorBrush(Color.FromRgb(0x14, 0x33, 0x39)) : Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        [Binding.DoNothing, Binding.DoNothing];
}

public sealed class StepNumberBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2 || values[0] is not int current || values[1] is not int step)
        {
            return new SolidColorBrush(Color.FromRgb(0x12, 0x2B, 0x3A));
        }

        if (step == current) return new SolidColorBrush(Color.FromRgb(0x16, 0xB6, 0xA5));
        return step < current ? new SolidColorBrush(Color.FromArgb(0x30, 0x16, 0xB6, 0xA5))
            : new SolidColorBrush(Color.FromRgb(0x12, 0x2B, 0x3A));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        [Binding.DoNothing, Binding.DoNothing];
}

public sealed class StepTextBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2 || values[0] is not int current || values[1] is not int step)
        {
            return new SolidColorBrush(Color.FromRgb(0x92, 0xA9, 0xB2));
        }

        if (step == current) return new SolidColorBrush(Color.FromRgb(0xEA, 0xFF, 0xFB));
        return step < current ? new SolidColorBrush(Color.FromRgb(0xD3, 0xE8, 0xE9))
            : new SolidColorBrush(Color.FromRgb(0x92, 0xA9, 0xB2));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        [Binding.DoNothing, Binding.DoNothing];
}
