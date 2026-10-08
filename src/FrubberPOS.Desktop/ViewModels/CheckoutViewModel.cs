using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Payment modal result. Cash takes received/change; transfer hides them
/// (spec §9, JHO-41). Amounts are whole COP (ADR-009).
/// </summary>
public sealed record CheckoutResult(bool Confirmed, bool IsCash, long ReceivedCop, long ChangeCop);

/// <summary>
/// Checkout dialog (JHO-41, spec §9, P-UX-008, rediseño JHO-67): discount ≤
/// subtotal, exclusive cash/transfer; cash requires sufficient received and
/// shows the three-state change box (M7), transfer hides received/change
/// and shows the exact-payment note. F9/Enter confirms, Esc cancels; the
/// confirm fires RequestClose once (B2).
/// </summary>
public sealed class CheckoutViewModel : INotifyPropertyChanged
{
    private long subtotalCop;
    private int itemCount;
    private string discountText = "0";
    private string receivedText = string.Empty;
    private bool isCash = true;
    private string errorMessage = string.Empty;
    private bool isConfirming;
    private bool isDiscountEditorVisible;
    private string changeTone = "neutral";
    private string changeLabelText = "Escribe el efectivo recibido";
    private string changeValueText = string.Empty;

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

    /// <summary>Recap row "Ítems": plain count (JHO-67, proto cobroSub2/cobroItems).</summary>
    public string ItemCountText => itemCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>Header subtitle: "N ítems · total" (JHO-67, proto cobroSub).</summary>
    public string ItemsSubtitleText =>
        $"{itemCount} {(itemCount == 1 ? "ítem" : "ítems")} · {TotalText}";

    /// <summary>Recap negative discount, same format as the TPV totals band.</summary>
    public string DiscountDisplayText => "-" + MoneyText.FormatCop(DiscountCop);

    /// <summary>Change box tone: "neutral" | "good" | "bad" (JHO-67 M7).</summary>
    public string ChangeTone => changeTone;

    public string ChangeLabelText => changeLabelText;

    public string ChangeValueText => changeValueText;

    /// <summary>M1 (JHO-67): the discount editor starts hidden unless F6.</summary>
    public bool IsDiscountEditorVisible => isDiscountEditorVisible;

    public string DiscountText
    {
        get => discountText;
        set
        {
            if (SetProperty(ref discountText, value))
            {
                Raise(nameof(TotalCop));
                Raise(nameof(TotalText));
                Raise(nameof(DiscountDisplayText));
                Raise(nameof(ItemsSubtitleText));
                RefreshChange();
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
                Raise(nameof(ChangeCop));
                Raise(nameof(ChangeText));
                RefreshChange();
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
                Raise(nameof(IsTransfer));
                Raise(nameof(ChangeCop));
                Raise(nameof(ChangeText));
                Raise(nameof(ShowCashFields));
                RefreshChange();
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

    /// <summary>
    /// B2 (JHO-67): true once a confirm succeeded; <see cref="RequestClose"/>
    /// fires at most once, so a second Enter/F9 never re-confirms (RN-VEN-002).
    /// </summary>
    public bool IsConfirming => isConfirming;

    public AsyncCommand ConfirmCommand { get; }

    public CheckoutResult Result { get; private set; } = new(false, true, 0, 0);

    public void Load(long subtotalCop, long discountCop, int itemCount = 0, bool revealDiscountEditor = false)
    {
        this.subtotalCop = subtotalCop;
        this.itemCount = itemCount;
        discountText = discountCop.ToString(CultureInfo.InvariantCulture);
        receivedText = string.Empty;
        isCash = true;
        errorMessage = string.Empty;
        isConfirming = false;
        isDiscountEditorVisible = revealDiscountEditor;
        RefreshChange();
        Raise(null);
        ConfirmCommand.RaiseCanExecuteChanged();
    }

    /// <summary>M1 (JHO-67): "Aplicar descuento (F6)" reveals the editor in place.</summary>
    public void RevealDiscountEditor()
    {
        if (isDiscountEditorVisible)
        {
            return;
        }

        isDiscountEditorVisible = true;
        Raise(nameof(IsDiscountEditorVisible));
    }

    /// <summary>M2 (JHO-67): quick amounts under the received input.</summary>
    public void ApplyQuickAmount(string? token)
    {
        if (string.Equals(token, "exacto", StringComparison.OrdinalIgnoreCase))
        {
            ReceivedText = TotalCop.ToString(CultureInfo.InvariantCulture);
            return;
        }

        if (!string.IsNullOrEmpty(token) && token.All(char.IsAsciiDigit))
        {
            ReceivedText = token;
        }
    }

    public void Confirm()
    {
        if (isConfirming)
        {
            return; // B2: RequestClose fires only once per confirmation.
        }

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

        isConfirming = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConfirming)));
        ConfirmCommand.RaiseCanExecuteChanged();
        RequestClose?.Invoke(this, true);
    }

    public void Cancel()
    {
        if (isConfirming)
        {
            return; // B2: the confirmed dialog never closes a second time.
        }

        RequestClose?.Invoke(this, false);
    }

    private bool CanConfirm()
    {
        if (isConfirming)
        {
            return false;
        }

        if (!MoneyText.TryParseCop(DiscountText, out var discount) || discount < 0 || discount > subtotalCop)
        {
            return false;
        }

        return !IsCash || (MoneyText.TryParseCop(ReceivedText, out var received) && received >= TotalCop);
    }

    /// <summary>
    /// M7 (JHO-67): the change box has three states per the prototype
    /// (calcCambio): neutral prompt / transfer note, "Pago exacto" or
    /// "Cambio a entregar" on good, "Faltan $ X" on bad.
    /// </summary>
    private void RefreshChange()
    {
        if (!IsCash)
        {
            changeTone = "neutral";
            changeLabelText = "Pago exacto por transferencia";
            changeValueText = TotalText;
        }
        else if (!MoneyText.TryParseCop(ReceivedText, out var received))
        {
            changeTone = "neutral";
            changeLabelText = "Escribe el efectivo recibido";
            changeValueText = string.Empty;
        }
        else if (received < TotalCop)
        {
            changeTone = "bad";
            changeLabelText = "Faltan";
            changeValueText = MoneyText.FormatCop(TotalCop - received);
        }
        else if (received == TotalCop)
        {
            changeTone = "good";
            changeLabelText = "Pago exacto ✓";
            changeValueText = MoneyText.FormatCop(0);
        }
        else
        {
            changeTone = "good";
            changeLabelText = "Cambio a entregar";
            changeValueText = MoneyText.FormatCop(received - TotalCop);
        }

        Raise(nameof(ChangeTone));
        Raise(nameof(ChangeLabelText));
        Raise(nameof(ChangeValueText));
    }

    private void Raise(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
