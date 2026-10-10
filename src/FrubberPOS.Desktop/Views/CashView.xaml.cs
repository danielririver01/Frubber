using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Cash screen content (JHO-36). Refreshes on every load so the session is
/// current after visiting other pages (JHO-82, same as TpvView); the
/// ViewModel owns logic.
/// </summary>
public partial class CashView : System.Windows.Controls.UserControl
{
    private readonly CashViewModel viewModel;

    public CashView(CashViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await viewModel.RefreshAsync();
    }
}
