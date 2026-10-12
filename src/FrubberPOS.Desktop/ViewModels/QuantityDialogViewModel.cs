using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Scale;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Quantity capture dialog for KG/LB sales (P-UX-009): up to three
/// decimals allowed, reject zero/negative; esc cancela. The view owns
/// focus; this VM owns the parse (QuantityText) and validation text.
/// JHO-86: with a configured scale, "Leer báscula" fills the field with
/// the stable reading; any failure keeps manual entry intact.
/// </summary>
public sealed class QuantityDialogViewModel : INotifyPropertyChanged
{
    private readonly ReadScaleWeightUseCase? scaleWeight;
    private ProductDto? product;
    private string quantityText = string.Empty;
    private string errorMessage = string.Empty;
    private string statusMessage = string.Empty;

    public QuantityDialogViewModel(ReadScaleWeightUseCase? scaleWeight = null)
    {
        this.scaleWeight = scaleWeight;
        ConfirmCommand = new AsyncCommand(_ => { Confirm(); return Task.CompletedTask; }, () => QuantityText.Trim().Length > 0);
        ReadScaleCommand = new AsyncCommand(_ => ReadScaleAsync(), () => scaleWeight is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fired to close the dialog. <c>true</c> = user confirmed with a valid amount.</summary>
    public event EventHandler<bool>? RequestClose;

    public string ProductLabel => product is null ? string.Empty : $"{product.Name} ({product.Plu})";

    public string UnitCode => product is null ? string.Empty : Domain.Products.ProductUnits.ToCode(product.Unit);

    public string QuantityText
    {
        get => quantityText;
        set
        {
            if (SetProperty(ref quantityText, value))
            {
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    /// <summary>Result of the last scale read (green info line; empty when none).</summary>
    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    /// <summary>Visible only when the app can read a scale (use case wired, JHO-86)
    /// and the product sells by weight.</summary>
    public bool ScaleAvailable => scaleWeight is not null
        && product?.Unit is Domain.Products.ProductUnit.Kg or Domain.Products.ProductUnit.Lb;

    public AsyncCommand ConfirmCommand { get; }

    public AsyncCommand ReadScaleCommand { get; }

    /// <summary>Reads the scale and fills the text box; keeps manual entry on failure.</summary>
    public async Task ReadScaleAsync()
    {
        if (scaleWeight is null || product is null)
        {
            return;
        }

        StatusMessage = "Leyendo báscula…";
        ErrorMessage = string.Empty;
        var result = await scaleWeight.ExecuteAsync(new ReadScaleWeightRequest(UnitCode), CancellationToken.None);
        if (!result.IsSuccess)
        {
            StatusMessage = string.Empty;
            ErrorMessage = result.Error!.Message;
            return;
        }

        var milli = result.Value.QuantityMilli;
        QuantityText = milli % 1_000 == 0
            ? (milli / 1_000).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : (milli / 1_000M).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        ErrorMessage = string.Empty;
        StatusMessage = "Peso tomado de la báscula.";
    }

    public long ParsedMilli { get; private set; }

    public void Load(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);
        this.product = product;
        quantityText = string.Empty;
        errorMessage = string.Empty;
        statusMessage = string.Empty;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        ConfirmCommand.RaiseCanExecuteChanged();
    }

    public void Confirm()
    {
        if (product is null)
        {
            return;
        }

        if (!Common.QuantityText.TryParseToMilli(QuantityText, product.Unit, out var milli) || milli <= 0)
        {
            ErrorMessage = product.Unit == Domain.Products.ProductUnit.Unit
                ? "Escriba un número entero de unidades."
                : "Escriba una cantidad con hasta tres decimales, mayor que cero.";
            return;
        }

        ParsedMilli = milli;
        ErrorMessage = string.Empty;
        RequestClose?.Invoke(this, true);
    }

    public void Cancel() => RequestClose?.Invoke(this, false);

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
