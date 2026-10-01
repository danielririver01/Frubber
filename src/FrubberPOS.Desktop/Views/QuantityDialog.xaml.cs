using System.Windows;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>Quantity dialog for KG/LB (P-UX-009). Esc/close cancel; Enter or F9 confirm via IsDefault.</summary>
public partial class QuantityDialog : Window
{
    private readonly QuantityDialogViewModel viewModel;

    public QuantityDialog(QuantityDialogViewModel viewModel, Application.Products.ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Load(product);
        InitializeComponent();
        viewModel.RequestClose += (_, saved) =>
        {
            DialogResult = saved;
            Close();
        };
        Loaded += (_, _) => QuantityBox.Focus();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
