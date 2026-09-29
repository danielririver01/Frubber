using System.Configuration;
using System.Data;
using System.Windows;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Logging;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Startup;
using FrubberPOS.Desktop.Composition;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Infrastructure.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FrubberPOS.Desktop;

/// <summary>
/// Interaction logic for App.xaml. Sole Composition Root (P-DD-002, JHO-14)
/// and navigation owner (JHO-29): builds the container once, migrates the
/// local database, routes to first-start or login, and swaps windows.
/// Nothing else in the solution touches the provider. Single instance and
/// safe startup messaging per P-DD-004 (JHO-18).
/// </summary>
public partial class App : System.Windows.Application, INavigationService
{
    private SingleInstanceGuard? instanceGuard;
    private ServiceProvider? provider;
    private Window? currentWindow;

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

            provider = ServiceConfiguration.BuildProvider(ServiceConfiguration.ProductionAppPaths(), this);
            logger = provider.GetRequiredService<IAppLogger>();

            var database = provider.GetRequiredService<DatabaseOptions>();
            var migrated = provider.GetRequiredService<MigrationRunner>().MigrateAsync(database, CancellationToken.None).GetAwaiter().GetResult();
            if (!migrated.IsSuccess)
            {
                MessageBox.Show(migrated.Error!.Message, "Frubber POS", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            logger.LogInformation("APP_START", "Frubber POS iniciado.");

            var firstStart = provider.GetRequiredService<CheckFirstStartUseCase>()
                .ExecuteAsync(new CheckFirstStartRequest(), CancellationToken.None).GetAwaiter().GetResult();
            if (!firstStart.IsSuccess)
            {
                MessageBox.Show(firstStart.Error!.Message, "Frubber POS", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            if (firstStart.Value.IsFirstStart)
            {
                ShowFirstStart();
            }
            else
            {
                ShowLogin();
            }
        }
        catch (Exception ex)
        {
            // Last-resort handler: log BEFORE releasing anything, because
            // disposing the provider also disposes the logger singleton.
            // Logging itself may be what's broken, so the diagnostic write
            // is best-effort and never allowed to hide the safe message
            // (this is the one place where catching without rethrowing is
            // correct: there is no higher UI boundary).
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

            instanceGuard?.Dispose();
            instanceGuard = null;
            provider?.Dispose();
            provider = null;

            MessageBox.Show(
                $"Frubber POS no pudo iniciar. {detail}",
                "Frubber POS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    public void ShowFirstStart() => SwapWindow(new FirstStartWindow(Resolve<FirstStartViewModel>()));

    public void ShowLogin() => SwapWindow(new LoginWindow(Resolve<LoginViewModel>()));

    public void ShowShell(SignedInUser user) =>
        SwapWindow(new MainWindow(new MainViewModel(
            user,
            Resolve<SignOutUseCase>(),
            this)));

    protected override void OnExit(ExitEventArgs e)
    {
        provider?.Dispose();
        provider = null;
        instanceGuard?.Dispose();
        instanceGuard = null;
        base.OnExit(e);
    }

    private T Resolve<T>() where T : notnull => provider!.GetRequiredService<T>();

    private void SwapWindow(Window next)
    {
        var previous = currentWindow;
        currentWindow = next;
        next.Closed += (_, _) =>
        {
            if (ReferenceEquals(currentWindow, next))
            {
                Shutdown();
            }
        };
        previous?.Close();
        next.Show();
    }
}
