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
        // Prototipos HTML del dueño (2026-10-02): canvas #F7F8FA, danger #DC2626.
        Assert.Equal("#F7F8FA", ColorOf(theme, "AppBackgroundColor"));
        Assert.Equal("#DC2626", ColorOf(theme, "ErrorColor"));
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

    [Fact]
    public void HighFidelityTokens_ExistWithApprovedRadiiAndActions()
    {
        var text = File.ReadAllText(ThemePath);

        // Cards radio 12 (r-l) y controles radio 10 (r-m) del prototipo,
        // pastillas 999 (full), acciones TPV 44–48 (§4.4).
        Assert.Contains("CornerRadius\" Value=\"12\"", text, StringComparison.Ordinal);
        Assert.Contains("CornerRadius\" Value=\"10\"", text, StringComparison.Ordinal);
        Assert.Contains("CornerRadius\" Value=\"999\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PillMint\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"KpiCard\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AvatarCircle\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DialogCard\"", text, StringComparison.Ordinal);
        Assert.Contains("MinHeight\" Value=\"48", text, StringComparison.Ordinal); // BigPrimaryButton / PadButton
        // Texto TOTAL grande: el TPV usa 28 DIPs seminegrita (§4.3).
        var tpv = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "FrubberPOS.Desktop", "Views", "TpvView.xaml"));
        Assert.Contains("FontSize=\"28\"", tpv, StringComparison.Ordinal);
    }

    [Fact]
    public void RedesignV3Tokens_MarcaNavYEstados_Existen()
    {
        // Base visual del prototipo integrado del dueño (JHO-69).
        var text = File.ReadAllText(ThemePath);

        Assert.Contains("x:Key=\"BrandGradient\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AvatarGradient\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"NavMenuItem\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"NavKeyChip\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"NavCountBadge\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IconCheck\"", text, StringComparison.Ordinal);
        Assert.Contains("<Color x:Key=\"MintColor\">#E6FAF5</Color>", text, StringComparison.Ordinal);
        Assert.Contains("<Color x:Key=\"AccentColor\">#14B8A6</Color>", text, StringComparison.Ordinal);
        Assert.Contains("<Color x:Key=\"BorderSoftColor\">#EEF1F4</Color>", text, StringComparison.Ordinal);
        Assert.Contains("<Color x:Key=\"TextSubColor\">#64748B</Color>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RedesignR2Tokens_ExistentEnClaroYOscuro()
    {
        // JHO-67 · B1: tokens del rediseño Venta/TPV + cobro, con paridad
        // claro/oscuro (los diccionarios deben declarar las mismas llaves).
        var light = File.ReadAllText(ThemePath);
        var darkPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "FrubberPOS.Desktop", "Themes", "FrubberTheme.Dark.xaml");
        var dark = File.ReadAllText(darkPath);

        Assert.Contains("<Color x:Key=\"WarnBorderColor\">#FDE68A</Color>", light, StringComparison.Ordinal);
        Assert.Contains("<Color x:Key=\"DisabledSurfaceColor\">#CBD5E1</Color>", light, StringComparison.Ordinal);
        Assert.Contains("<Color x:Key=\"ToastSurfaceColor\">#0F172A</Color>", light, StringComparison.Ordinal);
        Assert.Contains("<Color x:Key=\"PillNeutralColor\">#F1F5F9</Color>", light, StringComparison.Ordinal);
        Assert.Contains("<Color x:Key=\"OnBrandColor\">#FFFFFF</Color>", light, StringComparison.Ordinal);

        foreach (var key in new[]
                 {
                     "WarnBorderBrush", "DisabledSurfaceBrush", "DisabledTextBrush",
                     "ToastSurfaceBrush", "ToastTextBrush", "PillNeutralBrush",
                     "PillNeutralTextBrush", "OnBrandBrush",
                 })
        {
            Assert.Contains($"x:Key=\"{key}\"", light, StringComparison.Ordinal);
            Assert.Contains($"x:Key=\"{key}\"", dark, StringComparison.Ordinal);
        }

        // El deshabilitado de la acción principal es sólido, sin atenuar.
        Assert.Contains("x:Key=\"BigPrimaryButton\"", light, StringComparison.Ordinal);
        Assert.Contains("DisabledSurfaceBrush", light, StringComparison.Ordinal);
    }

    [Fact]
    public void DarkTheme_EstaPreparadoPeroNoSeFusiona_Todavia()
    {
        var darkPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "FrubberPOS.Desktop", "Themes", "FrubberTheme.Dark.xaml");
        Assert.True(File.Exists(darkPath), $"Falta {darkPath}");

        var dark = File.ReadAllText(darkPath);
        Assert.Contains("#0B1220", dark, StringComparison.Ordinal); // app-bg oscuro
        Assert.Contains("#E6EAF2", dark, StringComparison.Ordinal); // texto alto oscuro
        Assert.Contains("MintColor", dark, StringComparison.Ordinal);
        Assert.Contains("AccentColor", dark, StringComparison.Ordinal);

        // El claro sigue siendo el tema activo: App.xaml solo fusiona FrubberTheme.xaml.
        var app = File.ReadAllText(AppXamlPath);
        Assert.DoesNotContain("FrubberTheme.Dark.xaml", app, StringComparison.Ordinal);
    }
}
