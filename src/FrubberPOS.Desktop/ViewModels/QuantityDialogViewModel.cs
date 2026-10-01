using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Quantity capture dialog for KG/LB sales (P-UX-009): up to three
/// decimals allowed, reject zero/negative; esc cancela. The view owns
/// focus; this VM owns the parse (QuantityText) and validation text.
/// </summary>
public sealed class QuantityDialogViewModel : INotifyPropertyChanged
{
    private ProductDto? product;
    private string quantityText = string.Empty;
    private string errorMessage = string.Empty;

    public QuantityDialogViewModel()
    {
        ConfirmCommand = new AsyncCommand(_ => { Confirm(); return Task.CompletedTask; }, () => QuantityText.Trim().Length > 0);
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

    public AsyncCommand ConfirmCommand { get; }

    public long ParsedMilli { get; private set; }

    public void Load(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);
        this.product = product;
        quantityText = string.Empty;
        errorMessage = string.Empty;
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
