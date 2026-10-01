using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Payment modal result. Cash takes received/change; transfer hides them
/// (spec §9, JHO-41). Amounts are whole COP (ADR-009).
/// </summary>
public sealed record CheckoutResult(bool Confirmed, bool IsCash, long ReceivedCop, long ChangeCop);

/// <summary>
/// Checkout dialog (JHO-41, spec §9, P-UX-008): discount ≤ subtotal,
/// exclusive cash/transfer; cash requires sufficient received and shows
/// change, transfer hides received/change. F9/Enter confirms, Esc cancels.
/// </summary>
public sealed class CheckoutViewModel : INotifyPropertyChanged
{
    private long subtotalCop;
    private string discountText = "0";
    private string receivedText = string.Empty;
    private bool isCash = true;
    private string errorMessage = string.Empty;

    public CheckoutViewModel()
    {
        ConfirmCommand = new AsyncCommand(_ => { Confirm(); return Task.CompletedTask; }, () => CanConfirm());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fired to close the dialog. <c>true</c> = confirmed.</summary>
    public event EventHandler<bool>? RequestClose;

    public string SubtotalText => MoneyText.FormatCop(subtotalCop);

    public long DiscountCop => MoneyText.TryParseCop(DiscountText, out var d) ? d : 0;

    public long TotalCop => Math.Max(0, subtotalCop - DiscountCop);

    public string TotalText => MoneyText.FormatCop(TotalCop);

    public long ReceivedCop => MoneyText.TryParseCop(ReceivedText, out var r) ? r : 0;

    public long ChangeCop => IsCash ? Math.Max(0, ReceivedCop - TotalCop) : 0;

    public string ChangeText => MoneyText.FormatCop(ChangeCop);

    public string DiscountText
    {
        get => discountText;
        set
        {
            if (SetProperty(ref discountText, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalCop)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalText)));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ReceivedText
    {
        get => receivedText;
        set
        {
            if (SetProperty(ref receivedText, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChangeCop)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChangeText)));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCash
    {
        get => isCash;
        set
        {
            if (SetProperty(ref isCash, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTransfer)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChangeCop)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChangeText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCashFields)));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsTransfer
    {
        get => !IsCash;
        set => IsCash = !value;
    }

    public bool ShowCashFields => IsCash;

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public AsyncCommand ConfirmCommand { get; }

    public CheckoutResult Result { get; private set; } = new(false, true, 0, 0);

    public void Load(long subtotalCop, long discountCop)
    {
        this.subtotalCop = subtotalCop;
        discountText = discountCop.ToString(System.Globalization.CultureInfo.InvariantCulture);
        receivedText = string.Empty;
        isCash = true;
        errorMessage = string.Empty;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        ConfirmCommand.RaiseCanExecuteChanged();
    }

    public void Confirm()
    {
        ErrorMessage = string.Empty;

        if (!MoneyText.TryParseCop(DiscountText, out var discount) || discount < 0 || discount > subtotalCop)
        {
            ErrorMessage = "El descuento debe ser un valor entre cero y el subtotal.";
            return;
        }

        if (IsCash)
        {
            if (!MoneyText.TryParseCop(ReceivedText, out var received) || received < TotalCop)
            {
                ErrorMessage = "El efectivo recibido no alcanza el total.";
                return;
            }

            Result = new CheckoutResult(true, IsCash: true, received, received - TotalCop);
        }
        else
        {
            Result = new CheckoutResult(true, IsCash: false, 0, 0);
        }

        RequestClose?.Invoke(this, true);
    }

    public void Cancel() => RequestClose?.Invoke(this, false);

    private bool CanConfirm()
    {
        if (!MoneyText.TryParseCop(DiscountText, out var discount) || discount < 0 || discount > subtotalCop)
        {
            return false;
        }

        return !IsCash || (MoneyText.TryParseCop(ReceivedText, out var received) && received >= TotalCop);
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
