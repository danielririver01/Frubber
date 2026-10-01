using System.Windows;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Checkout dialog (JHO-41, P-UX-008): Enter/F9 confirms via IsDefault;
/// Esc or Cancel close without confirming. The ViewModel owns rules.
/// </summary>
public partial class CheckoutDialog : Window
{
    private readonly CheckoutViewModel viewModel;

    public CheckoutDialog(CheckoutViewModel viewModel, long subtotalCop, long discountCop)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Load(subtotalCop, discountCop);
        InitializeComponent();
        viewModel.RequestClose += (_, confirmed) =>
        {
            DialogResult = confirmed;
            Close();
        };
        Loaded += (_, _) => DiscountBox.Focus();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
