using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Scale;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Domain.Products;
using QtyFormat = FrubberPOS.Desktop.Common.QuantityText;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Quantity capture dialog for KG/LB sales (P-UX-009): up to three
/// decimals allowed, reject zero/negative; esc cancela. The view owns
/// focus; this VM owns the parse (QuantityText) and the validation text.
/// JHO-86: with a configured scale, "Leer báscula" fills the field with
/// the stable reading; any failure keeps manual entry intact.
/// JHO-94: modo «vender por monto» — el cajero teclea los pesos y el VM
/// deriva la cantidad con el precio del producto. El redondeo elige SIEMPRE
/// el gramo cuyo total no supera el monto tecleado y la vista previa
/// muestra el total real antes de confirmar. Solo aplica a KG/LB (las
/// UND siguen siendo piezas enteras).
/// </summary>
public sealed class QuantityDialogViewModel : INotifyPropertyChanged
{
    private readonly ReadScaleWeightUseCase? scaleWeight;
    private ProductDto? product;
    private string quantityText = string.Empty;
    private string amountText = string.Empty;
    private string errorMessage = string.Empty;
    private string statusMessage = string.Empty;
    private string previewMessage = string.Empty;
    private bool amountMode;

    public QuantityDialogViewModel(ReadScaleWeightUseCase? scaleWeight = null)
    {
        this.scaleWeight = scaleWeight;
        ConfirmCommand = new AsyncCommand(_ => { Confirm(); return Task.CompletedTask; }, () => CanConfirm);
        ReadScaleCommand = new AsyncCommand(_ => ReadScaleAsync(), () => scaleWeight is not null && !IsAmountMode);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fired to close the dialog. <c>true</c> = user confirmed with a valid amount.</summary>
    public event EventHandler<bool>? RequestClose;

    public string ProductLabel => product is null ? string.Empty : $"{product.Name} ({product.Plu})";

    public string UnitCode => product is null ? string.Empty : ProductUnits.ToCode(product.Unit);

    /// <summary>Shown under the product card so the cashier knows the price basis.</summary>
    public string PriceHint => product is null
        ? string.Empty
        : $"Precio: {MoneyText.FormatCop(product.PriceCop)} / {UnitCode}";

    /// <summary>JHO-94: el modo monto existe para KG y LB con precio &gt; 0.</summary>
    public bool AmountModeAvailable => product is { Unit: ProductUnit.Kg or ProductUnit.Lb, PriceCop: > 0 };

    public bool IsAmountMode => AmountMode && AmountModeAvailable;

    public bool IsQuantityMode => !IsAmountMode;

    public bool AmountMode
    {
        get => amountMode;
        set
        {
            var next = value && AmountModeAvailable;
            if (amountMode == next)
            {
                return;
            }

            amountMode = next;
            quantityText = string.Empty;
            amountText = string.Empty;
            errorMessage = string.Empty;
            statusMessage = string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QuantityText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AmountText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
            RefreshModeBindings();
            UpdatePreview();
            ConfirmCommand.RaiseCanExecuteChanged();
            ReadScaleCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Visible only when the app can read a scale (use case wired, JHO-86)
    /// and the sale is by weight in quantity mode.</summary>
    public bool ScaleAvailable => scaleWeight is not null
        && product?.Unit is ProductUnit.Kg or ProductUnit.Lb;

    public bool ScaleButtonVisible => ScaleAvailable && IsQuantityMode;

    public string DialogTitle => IsAmountMode ? "Vender por monto" : "Cantidad";

    public string InputHint => IsAmountMode ? "Monto a cobrar ($)" : $"Cantidad ({UnitCode})";

    public string QuantityText
    {
        get => quantityText;
        set
        {
            if (SetProperty(ref quantityText, value))
            {
                ConfirmCommand.RaiseCanExecuteChanged();
                UpdatePreview();
            }
        }
    }

    /// <summary>Pesos tecleados en el modo monto (JHO-94).</summary>
    public string AmountText
    {
        get => amountText;
        set
        {
            if (SetProperty(ref amountText, value))
            {
                ConfirmCommand.RaiseCanExecuteChanged();
                UpdatePreview();
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

    /// <summary>Vista previa en vivo (JHO-94): «0,500 KG → $ 2.000» / «Equivale a …».</summary>
    public string PreviewMessage
    {
        get => previewMessage;
        private set => SetProperty(ref previewMessage, value);
    }

    public bool HasPreview => !string.IsNullOrWhiteSpace(previewMessage);

    public AsyncCommand ConfirmCommand { get; }

    public AsyncCommand ReadScaleCommand { get; }

    public long ParsedMilli { get; private set; }

    private bool CanConfirm => IsAmountMode
        ? AmountText.Trim().Length > 0
        : QuantityText.Trim().Length > 0;

    /// <summary>Reads the scale and fills the text box; keeps manual entry on failure.</summary>
    public async Task ReadScaleAsync()
    {
        if (scaleWeight is null || product is null || IsAmountMode)
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

    public void Load(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);
        this.product = product;
        quantityText = string.Empty;
        amountText = string.Empty;
        amountMode = false;
        errorMessage = string.Empty;
        statusMessage = string.Empty;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        RefreshModeBindings();
        UpdatePreview();
        ConfirmCommand.RaiseCanExecuteChanged();
        ReadScaleCommand.RaiseCanExecuteChanged();
    }

    public void Confirm()
    {
        if (product is null)
        {
            return;
        }

        if (IsAmountMode)
        {
            ConfirmAmount();
            return;
        }

        if (!QtyFormat.TryParseToMilli(QuantityText, product.Unit, out var milli) || milli <= 0)
        {
            ErrorMessage = product.Unit == ProductUnit.Unit
                ? "Escriba un número entero de unidades."
                : "Escriba una cantidad con hasta tres decimales, mayor que cero.";
            return;
        }

        ParsedMilli = milli;
        ErrorMessage = string.Empty;
        RequestClose?.Invoke(this, true);
    }

    public void Cancel() => RequestClose?.Invoke(this, false);

    // ---------- JHO-94: monto → cantidad ----------

    private void ConfirmAmount()
    {
        if (product is null)
        {
            return;
        }

        if (!MoneyText.TryParseCop(AmountText, out var amount) || amount <= 0)
        {
            ErrorMessage = "Escriba un monto en pesos, mayor que cero.";
            return;
        }

        var milli = MilliFromAmount(amount, product.PriceCop);
        if (milli <= 0)
        {
            ErrorMessage = $"El monto debe ser al menos {MoneyText.FormatCop(MinimumAmount(product.PriceCop))} " +
                           $"(el precio de 0,001 {UnitCode}).";
            return;
        }

        ParsedMilli = milli;
        ErrorMessage = string.Empty;
        RequestClose?.Invoke(this, true);
    }

    /// <summary>
    /// Cantidad (milésimas) cuyo total es lo más cercano al monto tecleado
    /// sin superarlo nunca: redondeo al gramo hacia abajo si al subir un
    /// gramo el total ya pasaría el monto (JHO-94). Sin punto flotante.
    /// </summary>
    internal static long MilliFromAmount(long amountCop, long priceCop)
    {
        if (priceCop <= 0 || amountCop <= 0)
        {
            return 0;
        }

        var milli = amountCop * 1_000 / priceCop; // piso: total ≈ monto por debajo
        if (milli <= 0)
        {
            return 0;
        }

        // Sube mientras el siguiente gramo siga dentro del monto (tope de
        // seguridad para precios muy bajos: nunca más de 10.000 iteraciones).
        for (var guard = 0; guard < 10_000 && LineTotalCop(priceCop, milli + 1) <= amountCop; guard++)
        {
            milli++;
        }

        // …y baja si el piso ya se pasaba por el redondeo (nunca cobrar de más).
        while (milli > 0 && LineTotalCop(priceCop, milli) > amountCop)
        {
            milli--;
        }

        return milli;
    }

    /// <summary>Mismo redondeo de DraftSale.LineTotalCop (pesos enteros).</summary>
    internal static long LineTotalCop(long priceCop, long milli) =>
        (priceCop * milli + 500) / 1_000;

    /// <summary>Peso mínimo comprable: ceil(precio/1000), al menos $1.</summary>
    private static long MinimumAmount(long priceCop) => Math.Max(1, (priceCop + 999) / 1_000);

    private void UpdatePreview()
    {
        var next = string.Empty;
        if (product is not null)
        {
            if (IsAmountMode)
            {
                if (MoneyText.TryParseCop(AmountText, out var amount) && amount > 0)
                {
                    var milli = MilliFromAmount(amount, product.PriceCop);
                    next = milli > 0
                        ? $"Equivale a {QtyFormat.Format(milli, product.Unit)} {UnitCode} → " +
                          $"{MoneyText.FormatCop(LineTotalCop(product.PriceCop, milli))}"
                        : $"Mínimo {MoneyText.FormatCop(MinimumAmount(product.PriceCop))}";
                }
            }
            else if (QtyFormat.TryParseToMilli(QuantityText, product.Unit, out var milli) && milli > 0)
            {
                next = $"{QtyFormat.Format(milli, product.Unit)} {UnitCode} → " +
                       $"{MoneyText.FormatCop(LineTotalCop(product.PriceCop, milli))}";
            }
        }

        PreviewMessage = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
    }

    private void RefreshModeBindings()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AmountModeAvailable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAmountMode)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQuantityMode)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScaleButtonVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DialogTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputHint)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PriceHint)));
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
