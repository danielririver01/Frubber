using System.Windows;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Opening-balance correction (JHO-61): inline money input, Enter
/// confirms, Esc cancels. The value only takes effect after the
/// confirmation path in the use case.
/// </summary>
public partial class CorrectOpeningBalanceDialog : Window
{
    public CorrectOpeningBalanceDialog(long currentBalanceCop)
    {
        InitializeComponent();
        HintText.Text = $"El saldo actual es {MoneyText.FormatCop(currentBalanceCop)}."
            + " Solo puede corregirse antes de la primera venta de la caja.";
        AmountBox.Text = MoneyText.FormatCop(currentBalanceCop);
        Loaded += (_, _) =>
        {
            AmountBox.Focus();
            AmountBox.SelectAll();
        };
    }

    /// <summary>Monto corregido en COP; válido solo con DialogResult true.</summary>
    public long? CorrectedCop { get; private set; }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!MoneyText.TryParseCop(AmountBox.Text, out var value))
        {
            ErrorText.Text = "Escriba el saldo en pesos enteros.";
            return;
        }

        CorrectedCop = value;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnDragMove(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
