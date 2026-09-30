using System.Windows;
using System.Windows.Controls;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Product create/edit dialog (JHO-30). View-only concerns: PLU uppercases
/// on lost focus (spec §12.2); inverse boolean visibility keeps XAML free
/// of code. All decisions live in <see cref="ProductEditorViewModel"/>.
/// </summary>
public partial class ProductEditorDialog : Window
{
    private readonly ProductEditorViewModel viewModel;

    public ProductEditorDialog(ProductEditorViewModel viewModel, Application.Products.ProductDto? existing)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Load(existing);
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

    private void OnPluLostFocus(object sender, RoutedEventArgs e) => viewModel.NormalizePlu();
}
