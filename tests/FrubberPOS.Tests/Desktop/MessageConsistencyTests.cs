namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Unified messaging (JHO-50, P-UX-012/013): destructive confirmations
/// name the object and the consequence with an explicit action button —
/// never a bare "Sí" — and confirm with F9 / cancel with Esc. Technical
/// errors surface as safe messages (no SQL, stack traces or secrets).
/// </summary>
public sealed class MessageConsistencyTests
{
    private static readonly string DesktopDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FrubberPOS.Desktop");

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([DesktopDir, .. parts]));

    [Fact]
    public void DestructiveConfirmations_UseTheNamedActionDialog_NotBareYesNo()
    {
        var app = Read("App.xaml.cs");

        Assert.DoesNotContain("MessageBoxButton.YesNo", app, StringComparison.Ordinal);
        Assert.Contains("ConfirmActionDialog(", app, StringComparison.Ordinal);
        // Cada confirmación nombra la acción en el botón.
        Assert.Contains("\"Eliminar producto\"", app, StringComparison.Ordinal);
        Assert.Contains("\"Cerrar caja\"", app, StringComparison.Ordinal);
        Assert.Contains("\"Descartar venta\"", app, StringComparison.Ordinal);
        Assert.Contains("\"Anular venta\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmActionDialog_NamesObjectsAndActions_WithF9AndEsc()
    {
        var xaml = Read("Views", "ConfirmActionDialog.xaml");
        var code = Read("Views", "ConfirmActionDialog.xaml.cs");

        Assert.Contains("IsCancel=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"OnKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key.F9", code, StringComparison.Ordinal);
        Assert.Contains("no se puede deshacer", Read("App.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void UiFailures_NeverLeakTechnicalDetails()
    {
        var app = Read("App.xaml.cs");

        // El manejador global muestra un mensaje seguro y registra el detalle con código.
        Assert.Contains("Ocurrió un problema inesperado", app, StringComparison.Ordinal);
        Assert.Contains("LogError(", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message)", app, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Exception.Message", app, StringComparison.Ordinal);
    }
}
