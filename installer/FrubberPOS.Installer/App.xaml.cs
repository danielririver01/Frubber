using System.Windows;

namespace FrubberPOS.Installer;

public partial class App : Application
{
    /// <summary>--uninstall: desinstalar (desde "Agregar o quitar programas"). --quiet: sin preguntas.</summary>
    public static bool UninstallMode { get; private set; }
    public static bool QuietMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        UninstallMode = e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
        QuietMode = e.Args.Any(a => a.Equals("--quiet", StringComparison.OrdinalIgnoreCase));

        if (QuietMode && UninstallMode)
        {
            // Desinstalación silenciosa (sin UI): misma regla — datos jamás tocados.
            // Síncrono a propósito: nada de esperas que puedan colgar el hilo de UI.
            new InstallService().Uninstall(
                InstallService.GetInstalledLocation() ?? InstallService.DefaultInstallDir,
                new Progress<(double, string)>(_ => { }),
                CancellationToken.None);
            Shutdown();
            return;
        }

        if (UninstallMode)
        {
            new UninstallWindow().Show();
        }
        else
        {
            new MainWindow().Show();
        }

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "La instalación no pudo continuar: " + e.Exception.Message,
            "Frubber POS — Asistente de instalación",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
