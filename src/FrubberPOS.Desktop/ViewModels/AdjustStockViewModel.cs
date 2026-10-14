using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Scale;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Direct stock adjustment dialog (JHO-33): shows product, current stock
/// and asks only for the new stock — no reason (RN-INV-004). Unit tests
/// drive it without a window; the dialog closes via <see cref="RequestClose"/>.
/// JHO-92: «Leer báscula» fills the new stock for products sold by weight
/// (canasta o balde sobre el plato); the scale keeps its own TARE and the
/// field stays editable, so a failed read never blocks the manual path.
/// </summary>
public sealed class AdjustStockViewModel : INotifyPropertyChanged
{
    private readonly AdjustStockUseCase adjustStock;
    private readonly ReadScaleWeightUseCase? readScale;

    private ProductDto? product;
    private string newStockText = string.Empty;
    private string errorMessage = string.Empty;
    private string statusMessage = string.Empty;

    public AdjustStockViewModel(AdjustStockUseCase adjustStock, ReadScaleWeightUseCase? readScale = null)
    {
        ArgumentNullException.ThrowIfNull(adjustStock);
        this.adjustStock = adjustStock;
        this.readScale = readScale;

        SaveCommand = new AsyncCommand(_ => SaveAsync(), () => product is not null && NewStockText.Trim().Length > 0);
        ReadScaleCommand = new AsyncCommand(_ => ReadScaleAsync(), () => ScaleAvailable);
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

    /// <summary>Green info line for the scale read (same as the TPV dialog, JHO-86).</summary>
    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    /// <summary>JHO-92: only products sold by weight can be weighed.</summary>
    public bool ScaleAvailable => readScale is not null
        && product?.Unit is Domain.Products.ProductUnit.Kg or Domain.Products.ProductUnit.Lb;

    public AsyncCommand SaveCommand { get; }

    public AsyncCommand ReadScaleCommand { get; }

    /// <summary>Reads the scale and fills the new stock; keeps the manual value on failure.</summary>
    public async Task ReadScaleAsync()
    {
        if (product is null || readScale is null || !ScaleAvailable)
        {
            return;
        }

        StatusMessage = "Leyendo báscula…";
        ErrorMessage = string.Empty;
        var unitCode = Domain.Products.ProductUnits.ToCode(product.Unit);
        var result = await readScale.ExecuteAsync(new ReadScaleWeightRequest(unitCode), CancellationToken.None);
        if (!result.IsSuccess)
        {
            StatusMessage = string.Empty;
            ErrorMessage = result.Error!.Message;
            return;
        }

        NewStockText = QuantityText.Format(result.Value.QuantityMilli, product.Unit);
        ErrorMessage = string.Empty;
        StatusMessage = "Peso tomado de la báscula.";
    }

    public void Load(ProductDto product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        this.product = product;
        newStockText = QuantityText.Format(product.StockMilli, product.Unit);
        errorMessage = string.Empty;
        statusMessage = string.Empty;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProductLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentStockText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewStockText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorMessage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScaleAvailable)));
        SaveCommand.RaiseCanExecuteChanged();
        ReadScaleCommand.RaiseCanExecuteChanged();
    }

    public async Task SaveAsync()
    {
        if (product is null)
        {
            return;
        }

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
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
