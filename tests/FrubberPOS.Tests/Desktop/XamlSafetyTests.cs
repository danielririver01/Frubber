using System.Text.RegularExpressions;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Guards against XAML that compiles but explodes at runtime with a
/// XamlParseException (JHO-59 regression: Typography.NumeralStyle="Tabular"
/// crashed every entry to the TPV with "Tabular is not a valid value for
/// FontNumeralStyle"). WPF only accepts Default/Lining/OldStyle; tabular
/// figures come from the font itself (Inter/Segoe UI digits are tabular).
/// </summary>
public sealed class XamlSafetyTests
{
    private static readonly string DesktopRoot = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FrubberPOS.Desktop");

    private static IEnumerable<string> XamlFiles()
    {
        // GetFullPath colapsa ".." para que el filtro de bin/obj no se coma
        // las rutas relativas que devuelve EnumerateFiles.
        var root = Path.GetFullPath(DesktopRoot);
        Assert.True(Directory.Exists(root), $"No existe {root}");
        return Directory
            .EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
    }

    [Fact]
    public void NumeralStyle_OnlyUsesWpfSupportedValues()
    {
        var files = XamlFiles().ToList();
        Assert.True(files.Count > 0, "No se encontraron XAML en Desktop.");

        var allowed = new HashSet<string>(StringComparer.Ordinal) { "Default", "Lining", "OldStyle" };
        var attribute = new Regex("Typography\\.NumeralStyle=\"([^\"]+)\"");
        var setter = new Regex("Property=\"Typography\\.NumeralStyle\"\\s+Value=\"([^\"]+)\"");

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in attribute.Matches(text).Cast<Match>().Concat(setter.Matches(text).Cast<Match>()))
            {
                Assert.Contains(match.Groups[1].Value, allowed);
            }
        }
    }

    [Fact]
    public void EveryXamlFile_IsWellFormedXml()
    {
        foreach (var file in XamlFiles())
        {
            var ex = Record.Exception(() => System.Xml.Linq.XDocument.Load(file));
            Assert.True(ex is null, $"XAML mal formado: {file} — {ex?.Message}");
        }
    }
}
