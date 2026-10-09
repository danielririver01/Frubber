using System.Text.RegularExpressions;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// JHO-67 · B1 — "cero literales hex" en los XAML de vistas: los colores viven
/// como tokens en los diccionarios de tema (FrubberTheme.xaml / .Dark.xaml).
/// El conteo por archivo tiene un techo (deuda conocida) que solo puede bajar;
/// un archivo nuevo nace en 0. Los diccionarios de tema quedan fuera porque ahí
/// sí viven los literales (gradientes, plumas de iconos).
/// </summary>
public sealed class ViewColorTokenTests
{
    private static readonly string DesktopRoot = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FrubberPOS.Desktop");

    private static readonly Regex HexLiteral = new(
        "#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})(?![0-9A-Fa-f])",
        RegexOptions.Compiled);

    /// <summary>
    /// Techo actual (2026-10-07): literales heredados que este ticket no toca.
    /// Bajarlo es bienvenido; subirlo, no.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> Baseline =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["CashView.xaml"] = 1, // queda #2563EB (barra KPI Transferencia)
            ["LicenseWindow.xaml"] = 44,
            ["LoginCardView.xaml"] = 2,
            ["SplashWindow.xaml"] = 13,
        };

    /// <summary>Vistas que este ticket rediseña: sin un solo literal.</summary>
    private static readonly string[] TicketFiles =
    [
        "TpvView.xaml",
        "CheckoutDialog.xaml",
        "MainWindow.xaml",
    ];

    internal static int CountHexLiterals(string xaml) => HexLiteral.Matches(xaml).Count;

    private static IEnumerable<string> ViewXamlFiles()
    {
        var root = Path.GetFullPath(DesktopRoot);
        Assert.True(Directory.Exists(root), $"No existe {root}");
        return Directory
            .EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void Views_HexLiteralsNoCrecenSobreElTecho()
    {
        var files = ViewXamlFiles().ToList();
        Assert.True(files.Count > 0, "No se encontraron XAML de vistas en Desktop.");

        var violations = new List<string>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var count = CountHexLiterals(File.ReadAllText(file));
            var allowed = Baseline.TryGetValue(name, out var ceiling) ? ceiling : 0;
            if (count > allowed)
            {
                violations.Add($"{name}: {count} literales, techo {allowed}");
            }
        }

        Assert.True(violations.Count == 0,
            "Literales hex nuevos en vistas (usan tokens del tema en su lugar): "
            + string.Join("; ", violations));
    }

    [Fact]
    public void Views_TicketDelJho67_NoTieneLiterales()
    {
        foreach (var name in TicketFiles)
        {
            var file = Path.Combine(Path.GetFullPath(DesktopRoot), "Views", name);
            if (name == "MainWindow.xaml")
            {
                file = Path.Combine(Path.GetFullPath(DesktopRoot), name);
            }

            Assert.True(File.Exists(file), $"No existe {file}");
            var count = CountHexLiterals(File.ReadAllText(file));
            Assert.True(count == 0, $"{name} debe ir 100% con tokens; tiene {count} literales.");
        }
    }

    [Fact]
    public void CountHexLiterals_DetectaElLiteral_EIgnoraNumerosDeTicket()
    {
        // Negativo del lint: el detector ve #hex (6 y 8 dígitos) y NO se come
        // "#00124" de un número de ticket.
        const string synthetic = """
            <Border Background="#FDE68A" CornerRadius="6">
              <LinearGradientBrush>
                <GradientStop Color="#660F172A" Offset="0"/>
              </LinearGradientBrush>
              <TextBlock Text="Ticket #00124 · V-000043"/>
            </Border>
            """;

        Assert.Equal(2, CountHexLiterals(synthetic));
        Assert.Equal(0, CountHexLiterals("<TextBlock Text=\"Ticket #00124\"/>"));
        Assert.Equal(1, CountHexLiterals("<Border BorderBrush=\"#FFF\"/>"));
    }
}
