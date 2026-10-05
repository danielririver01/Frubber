using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop;

/// <summary>
/// Shell window: role-filtered menu plus page host. Composed by
/// <see cref="App"/> with its ViewModel; pages (products, cash) bind
/// <see cref="MainViewModel.CurrentPage"/> to the ContentControl.
/// F10 opens the cash screen (P-UX-011). Also hosts the JHO-64 lock
/// overlay (opaque, same window — never swapped) and the idle timer that
/// drives the approved auto-lock (3 min, warning at 30 s).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Bajo este ancho la barra lateral se compacta (JHO-52: se adapta antes de ocultar).</summary>
    private const double CompactWidthThreshold = 1000;

    private const double SidebarExpandedWidth = 200;
    private const double SidebarCompactWidth = 64;

    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer idleTimer;

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Focusable = true;

        idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        idleTimer.Tick += OnIdleTick;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < CompactWidthThreshold;
        Sidebar.Width = compact ? SidebarCompactWidth : SidebarExpandedWidth;

        // En modo compacto queda el primer carácter de cada sección:
        // la información crítica (menú) sigue operable sin ocultarse.
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RoleBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (LockLayer.Visibility == Visibility.Collapsed)
        {
            idleTimer.Start();
        }
    }

    private void OnIdleTick(object? sender, EventArgs e) => viewModel.OnIdleTick();

    /// <summary>Cualquier gesto del operador reinicia el ciclo de inactividad.</summary>
    private void OnUserActivity(object sender, EventArgs e)
    {
        if (LockLayer.Visibility == Visibility.Collapsed)
        {
            viewModel.NotifyUserActivity();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Mientras el overlay de bloqueo está activo el shell no recibe atajos.
        if (LockLayer.Visibility == Visibility.Visible)
        {
            return;
        }

        // F10 can arrive as a system key (WM_SYSKEYDOWN), so accept both forms.
        var isF10 = e.Key == Key.F10 || (e.Key == Key.System && e.SystemKey == Key.F10);
        if (!isF10)
        {
            return;
        }

        var caja = viewModel.MenuItems.FirstOrDefault(m => m.Title == "Caja");
        if (caja is not null)
        {
            viewModel.SelectedEntry = caja;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Overlay de bloqueo (JHO-64): pausa el shell sin cerrarlo, monta una
    /// tarjeta de acceso fresca en modo lock y lanza su banner de 2 s.
    /// </summary>
    public void ShowLock(LoginViewModel lockViewModel)
    {
        ArgumentNullException.ThrowIfNull(lockViewModel);

        idleTimer.Stop();
        LockCard.DataContext = lockViewModel;
        LockLayer.Visibility = Visibility.Visible;
        lockViewModel.ActivateLockMode();

        // La tarjeta nace vacía: igual que LoginWindow, carga sus usuarios
        // (con uno solo salta directo al PIN). Sin esto el bloqueo quedaría
        // sin filas (JHO-64).
        if (lockViewModel.Users.Count == 0)
        {
            _ = lockViewModel.LoadUsersAsync();
        }
    }

    /// <summary>Desbloqueo: reanuda el shell y reinicia el ciclo de inactividad.</summary>
    public void HideLock()
    {
        LockLayer.Visibility = Visibility.Collapsed;
        viewModel.NotifyUserActivity();
        idleTimer.Start();
    }

    /// <summary>El PIN que desbloquea pudo ser de otro rol (JHO-64).</summary>
    public void ApplySignedInUser(SignedInUser user) => viewModel.ApplyUser(user);
}
