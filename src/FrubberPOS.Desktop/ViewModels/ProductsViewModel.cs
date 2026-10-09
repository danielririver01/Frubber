using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Products and inventory screen (JHO-30, mockup fig. 6): search (F2),
/// Todos/Activos/Inactivos filter chips and a compact grid with PLU, name,
/// unit, price, stock and state. Row actions (edit, adjust stock, state
/// toggle, delete) act on the selected row. Deletion asks for confirmation
/// (spec §12.5); the use case refuses history and offers deactivation.
/// </summary>
public sealed class ProductsViewModel : INotifyPropertyChanged
{
    private readonly SearchProductsUseCase searchProducts;
    private readonly SetProductActiveStateUseCase setActive;
    private readonly DeleteProductUseCase deleteProduct;
    private readonly INavigationService navigation;

    private string searchText = string.Empty;
    private ProductStatusFilter filter = ProductStatusFilter.Todos;
    private string errorMessage = string.Empty;
    private ProductRowViewModel? selectedProduct;

    public ProductsViewModel(
        SearchProductsUseCase searchProducts,
        SetProductActiveStateUseCase setActive,
        DeleteProductUseCase deleteProduct,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(searchProducts);
        ArgumentNullException.ThrowIfNull(setActive);
        ArgumentNullException.ThrowIfNull(deleteProduct);
        ArgumentNullException.ThrowIfNull(navigation);
        this.searchProducts = searchProducts;
        this.setActive = setActive;
        this.deleteProduct = deleteProduct;
        this.navigation = navigation;

        SearchCommand = new AsyncCommand(_ => SearchAsync());
        NewCommand = new AsyncCommand(_ => NewAsync());
        EditCommand = new AsyncCommand(_ => EditAsync(), () => SelectedProduct is not null);
        AdjustStockCommand = new AsyncCommand(_ => AdjustStockAsync(), () => SelectedProduct is not null);
        ToggleActiveCommand = new AsyncCommand(_ => ToggleActiveAsync(), () => SelectedProduct is not null);
        DeleteCommand = new AsyncCommand(_ => DeleteAsync(), () => SelectedProduct is { Source.HasSalesHistory: false });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProductRowViewModel> Products { get; } = [];

    public string SearchText
    {
        get => searchText;
        set => SetProperty(ref searchText, value);
    }

    public ProductStatusFilter Filter => filter;

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public ProductRowViewModel? SelectedProduct
    {
        get => selectedProduct;
        set
        {
            if (SetProperty(ref selectedProduct, value))
            {
                EditCommand.RaiseCanExecuteChanged();
                AdjustStockCommand.RaiseCanExecuteChanged();
                ToggleActiveCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsFilterTodos => Filter == ProductStatusFilter.Todos;

    public bool IsFilterActivos => Filter == ProductStatusFilter.Activos;

    public bool IsFilterInactivos => Filter == ProductStatusFilter.Inactivos;

    public AsyncCommand SearchCommand { get; }

    public AsyncCommand NewCommand { get; }

    public AsyncCommand EditCommand { get; }

    public AsyncCommand AdjustStockCommand { get; }

    public AsyncCommand ToggleActiveCommand { get; }

    public AsyncCommand DeleteCommand { get; }

    public async Task SearchAsync()
    {
        var result = await searchProducts.ExecuteAsync(
            new SearchProductsRequest(SearchText, filter), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        var previousId = SelectedProduct?.ProductId;
        Products.Clear();
        foreach (var product in result.Value.Products)
        {
            Products.Add(ProductRowViewModel.From(product));
        }

        if (previousId.HasValue)
        {
            SelectedProduct = Products.FirstOrDefault(p => p.ProductId == previousId.Value);
        }
    }

    public async Task SetFilterAsync(ProductStatusFilter value)
    {
        if (filter == value)
        {
            return;
        }

        filter = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Filter)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFilterTodos)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFilterActivos)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFilterInactivos)));
        await SearchAsync();
    }

    public async Task NewAsync()
    {
        if (navigation.ShowProductEditor(null))
        {
            await SearchAsync();
        }
    }

    public async Task EditAsync()
    {
        if (SelectedProduct is not null && navigation.ShowProductEditor(SelectedProduct.Source))
        {
            await SearchAsync();
        }
    }

    public async Task AdjustStockAsync()
    {
        if (SelectedProduct is not null && navigation.ShowAdjustStock(SelectedProduct.Source))
        {
            await SearchAsync();
        }
    }

    public async Task ToggleActiveAsync()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        var next = !SelectedProduct.Source.IsActive;
        var result = await setActive.ExecuteAsync(
            new SetProductActiveStateRequest(SelectedProduct.ProductId, next), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        await SearchAsync();
    }

    public async Task DeleteAsync()
    {
        if (SelectedProduct is null || SelectedProduct.Source.HasSalesHistory)
        {
            return;
        }

        if (!navigation.ConfirmProductDeletion(SelectedProduct.Source))
        {
            return;
        }

        var result = await deleteProduct.ExecuteAsync(
            new DeleteProductRequest(SelectedProduct.ProductId), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        await SearchAsync();
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
