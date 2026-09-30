using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>Unit combo row: display code plus enum value.</summary>
public sealed record UnitOption(ProductUnit Unit)
{
    public string Code => ProductUnits.ToCode(Unit);
}

/// <summary>
/// Product editor dialog (JHO-30, spec §12.2/12.3): PLU, name, unit, price,
/// initial stock (create only) and active flag. Validation is per-field and
/// preserves what the user typed; the unit combo is disabled when the
/// product is locked, with the approved explanation. The use case keeps the
/// last word on business rules.
/// </summary>
public sealed class ProductEditorViewModel : INotifyPropertyChanged
{
    private readonly CreateProductUseCase createProduct;
    private readonly UpdateProductUseCase updateProduct;

    private ProductDto? existing;
    private string plu = string.Empty;
    private string name = string.Empty;
    private string priceText = string.Empty;
    private string stockText = "0";
    private bool isActive = true;
    private ProductUnit unitSelection = ProductUnit.Kg;
    private string errorMessage = string.Empty;

    public ProductEditorViewModel(CreateProductUseCase createProduct, UpdateProductUseCase updateProduct)
    {
        ArgumentNullException.ThrowIfNull(createProduct);
        ArgumentNullException.ThrowIfNull(updateProduct);
        this.createProduct = createProduct;
        this.updateProduct = updateProduct;

        SaveCommand = new AsyncCommand(_ => SaveAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fired when the dialog should close; <c>true</c> = saved.</summary>
    public event EventHandler<bool>? RequestClose;

    public bool IsEditing => existing is not null;

    public string Title => existing is null ? "Nuevo producto" : "Editar producto";

    public bool IsUnitEnabled => existing is null || !existing.UnitLocked;

    public bool UnitLockedHintVisible => !IsUnitEnabled;

    public string UnitLockedHint => "La unidad no puede cambiarse porque el producto tiene existencia o historial.";

    public bool IsStockEditable => existing is null;

    public string Plu
    {
        get => plu;
        set
        {
            // §12.2: PLU is shown uppercase.
            if (SetProperty(ref plu, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string PriceText
    {
        get => priceText;
        set => SetProperty(ref priceText, value);
    }

    public string StockText
    {
        get => stockText;
        set => SetProperty(ref stockText, value);
    }

    public bool IsActive
    {
        get => isActive;
        set => SetProperty(ref isActive, value);
    }

    public IReadOnlyList<UnitOption> Units { get; } =
    [
        new UnitOption(ProductUnit.Kg),
        new UnitOption(ProductUnit.Lb),
        new UnitOption(ProductUnit.Unit)
    ];

    public ProductUnit UnitSelection
    {
        get => unitSelection;
        set => SetProperty(ref unitSelection, value);
    }

    public UnitOption? SelectedUnit
    {
        get => new UnitOption(unitSelection);
        set
        {
            if (value is not null)
            {
                UnitSelection = value.Unit;
            }
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public AsyncCommand SaveCommand { get; }

    public void Load(ProductDto? product)
    {
        existing = product;
        if (product is not null)
        {
            plu = product.Plu;
            name = product.Name;
            unitSelection = product.Unit;
            priceText = product.PriceCop.ToString(System.Globalization.CultureInfo.InvariantCulture);
            stockText = QuantityText.Format(product.StockMilli, product.Unit);
            isActive = product.IsActive;
        }

        errorMessage = string.Empty;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        SaveCommand.RaiseCanExecuteChanged();
    }

    /// <summary>PLU is uppercased when the field loses focus (§12.2).</summary>
    public void NormalizePlu() => Plu = Plu.Trim().ToUpperInvariant();

    public async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (!MoneyText.TryParseCop(PriceText, out var priceCop) || priceCop <= 0)
        {
            ErrorMessage = "Escriba el precio en pesos, entero y mayor a cero.";
            return;
        }

        var stockMilli = 0L;
        if (existing is null && !QuantityText.TryParseToMilli(StockText, unitSelection, out stockMilli))
        {
            ErrorMessage = unitSelection == ProductUnit.Unit
                ? "La existencia inicial en unidades debe ser un número entero."
                : "Escriba la existencia inicial con hasta tres decimales.";
            return;
        }

        if (existing is null)
        {
            var created = await createProduct.ExecuteAsync(
                new CreateProductRequest(Plu, Name, unitSelection, priceCop, stockMilli), CancellationToken.None);
            if (!created.IsSuccess)
            {
                ErrorMessage = created.Error!.Message;
                return;
            }
        }
        else
        {
            var updated = await updateProduct.ExecuteAsync(
                new UpdateProductRequest(existing.ProductId, Plu, Name, unitSelection, priceCop), CancellationToken.None);
            if (!updated.IsSuccess)
            {
                ErrorMessage = updated.Error!.Message;
                return;
            }
        }

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
