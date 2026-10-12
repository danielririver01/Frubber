using System.Text.RegularExpressions;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Static guardrails for rules whose compliance is an ABSENCE that no
/// behavioral test can cover (JHO-57): the product never talks to the
/// network (RN-LIC-005/011, RN-OPE-001/004), never writes raw ESC/POS or
/// serial bytes to printers (RN-IMP-003) and never restores a backup from
/// the interface (RN-BKP-004). If any of those appear in src, this suite
/// fails at build-review time instead of in production.
/// </summary>
public sealed class ScopeGuardrailTests
{
    private static readonly string SrcRoot = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src");

    private static IEnumerable<string> SourceFiles()
    {
        var root = Path.GetFullPath(SrcRoot);
        Assert.True(Directory.Exists(root), $"No existe {root}");
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
    }

    [Theory]
    [InlineData("HttpClient")]
    [InlineData("HttpWebRequest")]
    [InlineData("System.Net.Sockets")]
    [InlineData("TcpClient")]
    [InlineData("WebSocket")]
    public void Source_NeverUsesTheNetwork(string forbidden)
    {
        foreach (var file in SourceFiles())
        {
            Assert.DoesNotContain(forbidden, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("SerialPort")]
    [InlineData("RawPrinterHelper")]
    [InlineData("0x1B")]           // byte ESC de ESC/POS
    [InlineData("\\u001B")]        // ESC como escape unicode
    public void Printing_NeverSpeaksEscPos(string forbidden)
    {
        foreach (var file in SourceFiles())
        {
            // JHO-86: el puerto serie SÍ se usa para la báscula (cambio de
            // alcance aprobado); la prohibición de hardware directo sigue
            // aplicando a la impresión. Los archivos de báscula quedan
            // exceptuados de la regla SerialPort.
            if (forbidden == "SerialPort"
                && (file.Contains(@"\Scale\") || file.Contains(@"\Composition\") || file.EndsWith("ScaleFrameParser.cs", StringComparison.Ordinal)))
            {
                continue;
            }

            Assert.DoesNotContain(forbidden, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Backup_NeverRestoresFromTheInterface()
    {
        var restoreWords = new Regex("RestoreDatabase|RestoreBackup|Restaurar copia|Restaurar copia de seguridad", RegexOptions.IgnoreCase);

        foreach (var file in SourceFiles())
        {
            Assert.False(restoreWords.IsMatch(File.ReadAllText(file)), $"Posible restauración en {file}");
        }
    }
}
