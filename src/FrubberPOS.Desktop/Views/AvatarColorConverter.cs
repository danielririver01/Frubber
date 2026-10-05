using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Color del avatar por usuario (prototipo login <c>AV_PAL</c>): 6 parejas
/// fondo/texto de la paleta, elegidas por hash estable del nombre para que
/// el mismo usuario tenga siempre el mismo color (lg-1, lo-7).
/// </summary>
public sealed class AvatarColorConverter : IValueConverter
{
    private static readonly (string Bg, string Fg)[] Palette =
    {
        ("#CCFBF1", "#0F766E"),
        ("#DBEAFE", "#1D4ED8"),
        ("#FCE7F3", "#BE185D"),
        ("#EDE9FE", "#6D28D9"),
        ("#FFEDD5", "#C2410C"),
        ("#DCFCE7", "#15803D"),
    };

    /// <summary>Índice determinista del nombre en la paleta (suma de caracteres).</summary>
    public static int IndexFor(string? name)
    {
        var sum = 0;
        foreach (var c in name ?? string.Empty)
        {
            sum += c;
        }

        return sum % Palette.Length;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var (bg, fg) = Palette[IndexFor(value as string)];
        var hex = parameter as string == "fg" ? fg : bg;
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
