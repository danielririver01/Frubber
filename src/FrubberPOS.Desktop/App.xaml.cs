using System.Configuration;
using System.Data;
using System.Windows;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Logging;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Startup;
using FrubberPOS.Application.Trial;
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

        DispatcherUnhandledException += OnDispatcherUnhandledException;

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

            // Runs off the UI thread on purpose: the trial evaluation touches
            // thread-pool work (WMI) that must never resume on the blocked
            // startup thread (deadlock). Other startup calls stay direct.
            var trial = Task.Run(() => provider.GetRequiredService<EvaluateTrialUseCase>()
                .ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None)).GetAwaiter().GetResult();
            if (!trial.IsSuccess)
            {
                MessageBox.Show(trial.Error!.Message, "Frubber POS", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            if (trial.Value.IsBlocked)
            {
                logger.LogInformation("APP_TRIAL", $"Bloqueado: {trial.Value.Outcome}.");
                var blockedViewModel = Resolve<TrialBlockedViewModel>();
                blockedViewModel.Setup(trial.Value.Outcome);
                if (new TrialBlockedWindow(blockedViewModel).ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }
            else if (trial.Value.IsLicensed)
            {
                logger.LogInformation("APP_TRIAL", "Licencia vitalicia activa.");
            }
            else
            {
                logger.LogInformation("APP_TRIAL", $"Prueba activa: quedan {trial.Value.DaysLeft} días.");
            }

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

    public void ShowLicense()
    {
        var window = new LicenseWindow(Resolve<LicenseActivationViewModel>());
        window.Owner = currentWindow;
        window.ShowDialog();
    }

    public void ShowShell(SignedInUser user) =>
        SwapWindow(new MainWindow(new MainViewModel(
            user,
            Resolve<SignOutUseCase>(),
            this,
            () => new ProductsView(Resolve<ProductsViewModel>()),
            () => new CashView(Resolve<CashViewModel>()),
            () => new TpvView(Resolve<TpvViewModel>()))));

    public bool ShowProductEditor(ProductDto? existing)
    {
        var window = new ProductEditorDialog(Resolve<ProductEditorViewModel>(), existing)
        {
            Owner = currentWindow
        };
        return window.ShowDialog() == true;
    }

    public bool ShowAdjustStock(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);
        var viewModel = Resolve<AdjustStockViewModel>();
        viewModel.Load(product);
        var window = new AdjustStockDialog(viewModel)
        {
            Owner = currentWindow
        };
        return window.ShowDialog() == true;
    }

    public bool ConfirmProductDeletion(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);
        var answer = MessageBox.Show(
            currentWindow,
            $"¿Eliminar «{product.Name}» ({product.Plu})? Esta acción no se puede deshacer.",
            "Frubber POS — Eliminar producto",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }

    public bool ConfirmCashClosure()
    {
        var answer = MessageBox.Show(
            currentWindow,
            "¿Cerrar la caja? Esta acción es irreversible y conservará el resumen histórico.",
            "Frubber POS — Cerrar caja",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            provider?.GetRequiredService<IAppLogger>().LogError("APP_UI_FAILURE", e.Exception.ToString());
        }
        catch
        {
            // Logging must never hide the safe message.
        }

        MessageBox.Show(
            "Ocurrió un problema inesperado. Intente de nuevo y si el problema continúa, contacte al soporte técnico.",
            "Frubber POS",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)    {
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
