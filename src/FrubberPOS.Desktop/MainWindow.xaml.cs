using System.Windows;
using System.Windows.Input;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop;

/// <summary>
/// Shell window: role-filtered menu plus page host. Composed by
/// <see cref="App"/> with its ViewModel; pages (products, cash) bind
/// <see cref="MainViewModel.CurrentPage"/> to the ContentControl.
/// F10 opens the cash screen (P-UX-011). Also hosts the JHO-64 lock
/// overlay (opaque, same window — never swapped). The lock is only
/// triggered manually via "Cerrar sesión" (dueño: no auto-lock).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Bajo este ancho la barra lateral se compacta (JHO-52: se adapta antes de ocultar).</summary>
    private const double CompactWidthThreshold = 1000;

    private const double SidebarExpandedWidth = 200;
    private const double SidebarCompactWidth = 64;

    private readonly MainViewModel viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Focusable = true;
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
    /// Solo se activa manualmente con "Cerrar sesión".
    /// </summary>
    public void ShowLock(LoginViewModel lockViewModel)
    {
        ArgumentNullException.ThrowIfNull(lockViewModel);

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

    /// <summary>Desbloqueo: reanuda el shell.</summary>
    public void HideLock()
    {
        LockLayer.Visibility = Visibility.Collapsed;
    }

    /// <summary>El PIN que desbloquea pudo ser de otro rol (JHO-64).</summary>
    public void ApplySignedInUser(SignedInUser user) => viewModel.ApplyUser(user);
}
