using System.Configuration;
using System.Data;
using System.Windows;
using FrubberPOS.Application.Logging;
using FrubberPOS.Desktop.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FrubberPOS.Desktop;

/// <summary>
/// Interaction logic for App.xaml. Sole Composition Root (P-DD-002, JHO-14):
/// builds the container once at startup and owns it until exit. Nothing else
/// in the solution touches the provider. Single instance and safe startup
/// messaging per P-DD-004 (JHO-18).
/// </summary>
public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? instanceGuard;
    private ServiceProvider? provider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IAppLogger? logger = null;
        try
        {
            if (!SingleInstanceGuard.TryAcquire(out instanceGuard))
            {
                MessageBox.Show(
                    "Frubber POS ya está abierto en este equipo.",
                    "Frubber POS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            provider = ServiceConfiguration.BuildProvider(ServiceConfiguration.ProductionLoggingOptions());
            logger = provider.GetRequiredService<IAppLogger>();
            logger.LogInformation("APP_START", "Frubber POS iniciado.");
        }
        catch (Exception ex)
        {
            instanceGuard?.Dispose();
            instanceGuard = null;
            provider?.Dispose();
            provider = null;

            // Last-resort handler: logging itself may be what's broken, so the
            // diagnostic write is best-effort and never allowed to hide the
            // safe message (this is the one place where catching without
            // rethrowing is correct: there is no higher UI boundary).
            var detail = "Intente de nuevo y si el problema continúa, contacte al soporte técnico.";
            if (logger is not null)
            {
                try
                {
                    detail = $"Intente de nuevo y si el problema continúa, informe el código {logger.LogError("APP_STARTUP_FAILURE", ex.ToString())} al soporte técnico.";
                }
                catch
                {
                    // Keep the generic safe message.
                }
            }

            MessageBox.Show(
                $"Frubber POS no pudo iniciar. {detail}",
                "Frubber POS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        provider?.Dispose();
        provider = null;
        instanceGuard?.Dispose();
        instanceGuard = null;
        base.OnExit(e);
    }
}

