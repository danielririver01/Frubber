using System.Windows.Input;
using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Products screen content (JHO-30, mockup fig. 6). View-only concerns:
/// F2 focuses the search box (P-UX-011) and the filter chips set the
/// ViewModel filter. No business logic here.
/// </summary>
public partial class ProductsView : System.Windows.Controls.UserControl
{
    private readonly ProductsViewModel viewModel;

    public ProductsView(ProductsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await viewModel.SearchAsync();
        SearchBox.Focus();
    }

    private async void OnFilterTodos(object sender, System.Windows.RoutedEventArgs e) =>
        await viewModel.SetFilterAsync(ProductStatusFilter.Todos);

    private async void OnFilterActivos(object sender, System.Windows.RoutedEventArgs e) =>
        await viewModel.SetFilterAsync(ProductStatusFilter.Activos);

    private async void OnFilterInactivos(object sender, System.Windows.RoutedEventArgs e) =>
        await viewModel.SetFilterAsync(ProductStatusFilter.Inactivos);
}
