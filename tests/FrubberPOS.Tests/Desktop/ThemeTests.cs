using System.Globalization;
using System.Xml.Linq;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Approved light theme (JHO-52, contact sheet): accent #0F766E, Segoe UI,
/// >= 40 DIP targets, visible focus, AA contrast on the key pairs, and the
/// theme merged application-wide from App.xaml.
/// </summary>
public sealed class ThemeTests
{
    private static readonly string ThemePath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "FrubberPOS.Desktop", "Themes", "FrubberTheme.xaml");
    private static readonly string AppXamlPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "FrubberPOS.Desktop", "App.xaml");

    private static XDocument LoadTheme()
    {
        Assert.True(File.Exists(ThemePath), $"No existe el tema {ThemePath}");
        return XDocument.Load(ThemePath);
    }

    private static string ColorOf(XDocument theme, string key)
    {
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var color = theme.Root!.Elements(ns + "Color")
            .Single(e => (string)e.Attribute(x + "Key")! == key);
        return (string?)color.Attribute("Color") ?? color.Value;
    }

    private static double RelativeLuminance(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
        var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
        var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
        double Channel(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Contrast(string hexA, string hexB)
    {
        var a = RelativeLuminance(hexA);
        var b = RelativeLuminance(hexB);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    [Fact]
    public void Theme_IsMergedApplicationWide()
    {
        var app = File.ReadAllText(AppXamlPath);
        Assert.Contains("Themes/FrubberTheme.xaml", app, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"PrimaryColor\"", app, StringComparison.Ordinal); // tokens viven en el tema
    }

    [Fact]
    public void Palette_DefinesTheApprovedTokens()
    {
        var theme = LoadTheme();
        Assert.Equal("#0F766E", ColorOf(theme, "PrimaryColor"));
        Assert.Equal("#F4F7F5", ColorOf(theme, "AppBackgroundColor"));
        Assert.Equal("#B91C1C", ColorOf(theme, "ErrorColor"));
    }

    [Fact]
    public void KeyPairs_MeetAAContrast()
    {
        var theme = LoadTheme();
        // Texto sobre superficie y acento con texto blanco (botones).
        Assert.True(Contrast(ColorOf(theme, "TextColor"), "#FFFFFF") >= 4.5, "Texto/superficie");
        Assert.True(Contrast(ColorOf(theme, "SecondaryTextColor"), ColorOf(theme, "AppBackgroundColor")) >= 4.5, "Texto secundario");
        Assert.True(Contrast("#FFFFFF", ColorOf(theme, "PrimaryColor")) >= 4.5, "Acento/blanco");
        Assert.True(Contrast(ColorOf(theme, "ErrorColor"), "#FFFFFF") >= 4.5, "Error/blanco");
    }

    [Fact]
    public void InteractiveElements_AreAtLeastFortyDips_WithVisibleFocus()
    {
        var text = File.ReadAllText(ThemePath);
        Assert.Contains("Segoe UI", text, StringComparison.Ordinal);
        // Botones, entradas, combos e ítems de menú >= 40 DIPs.
        Assert.True(text.Split("MinHeight\" Value=\"40").Length >= 4, "MinHeight 40 en Button/TextBox/ComboBox");
        Assert.Contains("MinHeight\" Value=\"44", text, StringComparison.Ordinal); // ítems del menú
        Assert.Contains("FocusVisualStyle", text, StringComparison.Ordinal);
        Assert.Contains("StrokeDashArray", text, StringComparison.Ordinal); // foco visible discontinuo
    }
}
