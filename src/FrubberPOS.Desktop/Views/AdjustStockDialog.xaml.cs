using System.Windows;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Direct stock adjustment dialog (JHO-33). The view owns only the close
/// gesture; all decisions live in <see cref="AdjustStockViewModel"/>.
/// </summary>
public partial class AdjustStockDialog : Window
{
    private readonly AdjustStockViewModel viewModel;

    public AdjustStockDialog(AdjustStockViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.RequestClose += OnRequestClose;
    }

    private void OnRequestClose(object? sender, bool saved)
    {
        DialogResult = saved;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
