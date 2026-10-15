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
        Loaded += (_, _) => FocusActiveBox();
    }

    /// <summary>JHO-94: el foco salta a la caja del modo activo.</summary>
    private void FocusActiveBox()
    {
        if (viewModel.IsAmountMode)
        {
            AmountBox.Focus();
        }
        else
        {
            QuantityBox.Focus();
        }
    }

    private void OnAmountModeChanged(object sender, RoutedEventArgs e)
    {
        FocusActiveBox();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnDragMove(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
