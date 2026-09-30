using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Direct stock adjustment dialog (JHO-33): shows product, current stock
/// and asks only for the new stock — no reason (RN-INV-004). Unit tests
/// drive it without a window; the dialog closes via <see cref="RequestClose"/>.
/// </summary>
public sealed class AdjustStockViewModel : INotifyPropertyChanged
{
    private readonly AdjustStockUseCase adjustStock;

    private ProductDto? product;
    private string newStockText = string.Empty;
    private string errorMessage = string.Empty;

    public AdjustStockViewModel(AdjustStockUseCase adjustStock)
    {
        ArgumentNullException.ThrowIfNull(adjustStock);
        this.adjustStock = adjustStock;

        SaveCommand = new AsyncCommand(_ => SaveAsync(), () => product is not null && NewStockText.Trim().Length > 0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fired when the dialog should close; <c>true</c> = saved.</summary>
    public event EventHandler<bool>? RequestClose;

    public string ProductLabel => product is null ? string.Empty : $"{product.Name} ({product.Plu})";

    public string CurrentStockText => product is null ? string.Empty : QuantityText.Format(product.StockMilli, product.Unit);

    public string NewStockText
    {
        get => newStockText;
        set
        {
            if (SetProperty(ref newStockText, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public AsyncCommand SaveCommand { get; }

    public void Load(ProductDto product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        this.product = product;
        newStockText = QuantityText.Format(product.StockMilli, product.Unit);
        errorMessage = string.Empty;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProductLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentStockText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewStockText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorMessage)));
        SaveCommand.RaiseCanExecuteChanged();
    }

    public async Task SaveAsync()
    {
        if (product is null)
        {
            return;
        }

        if (!QuantityText.TryParseToMilli(NewStockText, product.Unit, out var newStockMilli))
        {
            ErrorMessage = product.Unit == Domain.Products.ProductUnit.Unit
                ? "Escriba un número entero de unidades (puede ser negativo)."
                : "Escriba una cantidad válida con hasta tres decimales (puede ser negativa).";
            return;
        }

        var result = await adjustStock.ExecuteAsync(
            new AdjustStockRequest(product.ProductId, newStockMilli), CancellationToken.None);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        RequestClose?.Invoke(this, true);
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
