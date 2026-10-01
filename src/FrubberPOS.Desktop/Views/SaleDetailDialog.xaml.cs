using System.Windows;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Read-only historical detail (JHO-46, P-UX §13.3). Anular confirms with
/// F9 in its own dialog (P-UX §13.4); Esc closes without touching the sale.
/// </summary>
public partial class SaleDetailDialog : Window
{
    private readonly SaleDetailViewModel viewModel;

    public SaleDetailDialog(SaleDetailViewModel viewModel, long saleNumber, bool viewerIsAdmin)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.RequestClose += (_, voided) =>
        {
            DialogResult = voided;
            Close();
        };
        Loaded += async (_, _) => await viewModel.LoadAsync(saleNumber, viewerIsAdmin);
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        viewModel.Close();
    }
}
