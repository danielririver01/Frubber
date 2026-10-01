using System.Windows;
using System.Windows.Input;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop;

/// <summary>
/// Shell window: role-filtered menu plus page host. Composed by
/// <see cref="App"/> with its ViewModel; pages (products, cash) bind
/// <see cref="MainViewModel.CurrentPage"/> to the ContentControl.
/// F10 opens the cash screen (P-UX-011).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Bajo este ancho la barra lateral se compacta (JHO-52: se adapta antes de ocultar).</summary>
    private const double CompactWidthThreshold = 1000;

    private const double SidebarExpandedWidth = 180;
    private const double SidebarCompactWidth = 52;

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
        MenuList.Width = compact ? SidebarCompactWidth : SidebarExpandedWidth;

        // En modo compacto queda el primer carácter de cada sección:
        // la información crítica (menú) sigue operable sin ocultarse.
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
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
}
