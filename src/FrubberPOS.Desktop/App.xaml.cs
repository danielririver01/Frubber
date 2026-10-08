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
using FrubberPOS.Application.Users;
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
    private bool trialIsLicensed;
    private int trialDaysLeft = -1;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Splash JHO-69 (diseño del dueño): se muestra mientras corre el
        // arranque real y reporta la fase. Se cierra antes del login.
        var splash = new SplashWindow();
        splash.Show();
        splash.SetStatus("Iniciando sistema...");

        IAppLogger? logger = null;
        try
        {
            if (!SingleInstanceGuard.TryAcquire(out instanceGuard))
            {
                splash.Close();
                MessageBox.Show(
                    "Frubber POS ya está abierto en este equipo.",
                    "Frubber POS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            splash.SetStatus("Conectando a base de datos...");
            provider = ServiceConfiguration.BuildProvider(ServiceConfiguration.ProductionAppPaths(), this);
            logger = provider.GetRequiredService<IAppLogger>();

            splash.SetStatus("Aplicando migraciones...");
            var database = provider.GetRequiredService<DatabaseOptions>();
            var migrated = await provider.GetRequiredService<MigrationRunner>().MigrateAsync(database, CancellationToken.None);
            if (!migrated.IsSuccess)
            {
                splash.Close();
                MessageBox.Show(migrated.Error!.Message, "Frubber POS", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            logger.LogInformation("APP_START", "Frubber POS iniciado.");

            // Runs off the UI thread on purpose: the trial evaluation touches
            // thread-pool work (WMI) that must never resume on the blocked
            // startup thread (deadlock). Other startup calls stay direct.
            splash.SetStatus("Validando licencia criptográfica...");
            var trial = await Task.Run(() => provider.GetRequiredService<EvaluateTrialUseCase>()
                .ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None));
            if (!trial.IsSuccess)
            {
                splash.Close();
                MessageBox.Show(trial.Error!.Message, "Frubber POS", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            trialIsLicensed = trial.Value.IsLicensed;
            trialDaysLeft = trial.Value.DaysLeft;

            if (trial.Value.IsBlocked)
            {
                logger.LogInformation("APP_TRIAL", $"Bloqueado: {trial.Value.Outcome}.");
                splash.Close();
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

            splash.SetStatus("Preparando interfaz del TPV...");
            var firstStart = await provider.GetRequiredService<CheckFirstStartUseCase>()
                .ExecuteAsync(new CheckFirstStartRequest(), CancellationToken.None);
            if (!firstStart.IsSuccess)
            {
                splash.Close();
                MessageBox.Show(firstStart.Error!.Message, "Frubber POS", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // El splash se nota un instante y nunca oculta un error previo.
            await Task.Delay(650);
            splash.Close();

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
            splash.Close();
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

    /// <summary>
    /// Bloqueo (JHO-64): en lugar de sustituir la ventana se monta una
    /// tarjeta de acceso fresca sobre el shell. OnAuthenticated se redirige
    /// al desbloqueo: revalida identidad/rol en el MainViewModel existente y
    /// levanta la capa, sin recrear el shell (el borrador y la página viva
    /// siguen intactos debajo).
    /// </summary>
    public void ShowLock()
    {
        if (currentWindow is not MainWindow main)
        {
            return;
        }

        var lockViewModel = Resolve<LoginViewModel>();
        lockViewModel.OnAuthenticated = user =>
        {
            main.ApplySignedInUser(user);
            main.HideLock();
            return Task.CompletedTask;
        };
        main.ShowLock(lockViewModel);
    }

    public void ShowShell(SignedInUser user)
    {
        var licenseBadge = trialIsLicensed
            ? "Licencia vitalicia"
            : trialDaysLeft is >= 0 ? $"Prueba · {trialDaysLeft} días" : null;
        // Un solo TpvViewModel para la sesión: alimenta la página y el badge
        // del menú Venta con las líneas del borrador (JHO-69).
        var tpvViewModel = Resolve<TpvViewModel>();
        SwapWindow(new MainWindow(new MainViewModel(
            user,
            Resolve<SignOutUseCase>(),
            this,
            () => new ProductsView(Resolve<ProductsViewModel>()),
            () => new CashView(Resolve<CashViewModel>()),
            () => new TpvView(tpvViewModel),
            () => new SalesHistoryView(Resolve<SalesHistoryViewModel>()),
            () => new BusinessSettingsView(Resolve<BusinessSettingsViewModel>()),
            () => new BackupView(Resolve<BackupViewModel>()),
            licenseBadge,
            async cancellationToken =>
            {
                var open = await provider!.GetRequiredService<Application.Cash.GetOpenCashSessionUseCase>()
                    .ExecuteAsync(new Application.Cash.GetOpenCashSessionRequest(), cancellationToken);
                // JHO-67: el id de sesión alimenta el subtítulo "Sesión #0042".
                return open.IsSuccess ? open.Value.Session?.CashSessionId : (int?)null;
            },
            trialIsLicensed,
            () => new UsersView(Resolve<UsersViewModel>()),
            tpvViewModel)));
    }

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
        var dialog = new ConfirmActionDialog(
            "Eliminar producto",
            $"¿Eliminar «{product.Name}» ({product.Plu})? El producto se quitará del catálogo y esta acción no se puede deshacer. (F9 confirma)",
            "Eliminar producto")
        {
            Owner = currentWindow
        };
        return dialog.ShowDialog() == true;
    }

    public bool ConfirmCashClosure()
    {
        var dialog = new ConfirmActionDialog(
            "Cerrar caja",
            "¿Cerrar la caja? El cierre es irreversible y conservará el resumen histórico de la sesión. (F9 confirma)",
            "Cerrar caja")
        {
            Owner = currentWindow
        };
        return dialog.ShowDialog() == true;
    }

    public bool ConfirmDiscardDraft()
    {
        var dialog = new ConfirmActionDialog(
            "Descartar venta",
            "¿Descartar esta venta? Se perderán las líneas en preparación. (F9 confirma)",
            "Descartar venta")
        {
            Owner = currentWindow
        };
        return dialog.ShowDialog() == true;
    }

    public long? AskQuantity(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);
        var viewModel = Resolve<QuantityDialogViewModel>();
        var window = new QuantityDialog(viewModel, product)
        {
            Owner = currentWindow
        };
        return window.ShowDialog() == true ? viewModel.ParsedMilli : null;
    }

    public CheckoutResultDto ShowCheckout(long subtotalCop, long pendingDiscountCop, int itemCount, bool discountMode)
    {
        var viewModel = Resolve<CheckoutViewModel>();
        var window = new CheckoutDialog(viewModel, subtotalCop, pendingDiscountCop, itemCount, discountMode)
        {
            Owner = currentWindow
        };
        if (window.ShowDialog() != true)
        {
            return new CheckoutResultDto(false, "CASH", 0, 0, 0);
        }

        var result = viewModel.Result;
        return new CheckoutResultDto(true, result.IsCash ? "CASH" : "TRANSFER", result.ReceivedCop, result.ChangeCop, viewModel.DiscountCop);
    }

    public bool ShowSaleSaved(SaleSavedSummary sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        var window = new SaleSavedDialog(sale)
        {
            Owner = currentWindow
        };
        window.ShowDialog();
        return window.PrintRequested;
    }

    public string? AskPrinter(string? configuredPrinterName)
    {
        var window = new PrintTicketDialog(
            Common.WindowsTicketPrinter.InstalledPrinters(), configuredPrinterName)
        {
            Owner = currentWindow
        };
        return window.ShowDialog() == true ? window.SelectedPrinterName : null;
    }

    public bool ShowSaleDetail(long saleNumber)
    {
        var viewModel = Resolve<SaleDetailViewModel>();
        var viewerIsAdmin = provider!.GetRequiredService<ICurrentSession>().Current?.Role == Domain.Users.UserRole.Admin;
        var window = new SaleDetailDialog(viewModel, saleNumber, viewerIsAdmin)
        {
            Owner = currentWindow
        };
        return window.ShowDialog() == true;
    }

    public bool ConfirmSaleVoid(long saleNumber)
    {
        var dialog = new ConfirmActionDialog(
            "Anular venta",
            $"¿Anular la venta V-{saleNumber:D6}? Se reintegrará el inventario vendido y la venta quedará"
                + " marcada como anulada en el historial. Esta acción no se puede deshacer. (F9 confirma)",
            "Anular venta")
        {
            Owner = currentWindow
        };
        return dialog.ShowDialog() == true;
    }

    public string? AskBackupDestination()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Frubber POS — Crear respaldo",
            Filter = "Respaldo Frubber POS (*.db)|*.db",
            DefaultExt = ".db",
            AddExtension = true,
            FileName = $"FrubberPOS-{DateTime.Now:yyyyMMdd-HHmmss}.db",
            OverwritePrompt = true
        };
        return dialog.ShowDialog(currentWindow) == true ? dialog.FileName : null;
    }

    public bool ShowUserEditor(UserDetailDto? existing)
    {
        var window = new UserEditorDialog(Resolve<UserEditorViewModel>(), existing)
        {
            Owner = currentWindow
        };
        return window.ShowDialog() == true;
    }

    public bool ShowPinReset(UserDetailDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var window = new ResetPinDialog(Resolve<ResetPinViewModel>(), user)
        {
            Owner = currentWindow
        };
        return window.ShowDialog() == true;
    }

    public bool ConfirmUserDeactivation(UserDetailDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var dialog = new ConfirmActionDialog(
            "Desactivar usuario",
            $"¿Desactivar a «{user.DisplayName}»? No podrá ingresar hasta que un administrador lo reactive. Su historial se conserva. (F9 confirma)",
            "Desactivar usuario")
        {
            Owner = currentWindow
        };
        return dialog.ShowDialog() == true;
    }

    public bool ConfirmUserDeletion(UserDetailDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var dialog = new ConfirmActionDialog(
            "Eliminar usuario",
            $"¿Eliminar a «{user.DisplayName}»? Si tiene historial se desactivará en su lugar. Esta acción no se puede deshacer. (F9 confirma)",
            "Eliminar usuario")
        {
            Owner = currentWindow
        };
        return dialog.ShowDialog() == true;
    }

    public long? AskOpeningBalanceCorrection(long currentBalanceCop)
    {
        var dialog = new CorrectOpeningBalanceDialog(currentBalanceCop)
        {
            Owner = currentWindow
        };
        return dialog.ShowDialog() == true ? dialog.CorrectedCop : null;
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
