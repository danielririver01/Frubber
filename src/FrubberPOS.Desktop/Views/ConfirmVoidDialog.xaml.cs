using System.Windows;
using System.Windows.Input;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Void confirmation (JHO-46, P-UX §13.4, RN-HIS-010): advierte el
/// reintegro de inventario y no pide motivo. F9 confirma; Esc cancela.
/// </summary>
public partial class ConfirmVoidDialog : Window
{
    public ConfirmVoidDialog(long saleNumber)
    {
        InitializeComponent();
        MessageText.Text =
            $"¿Anular la venta V-{saleNumber:D6}? Se reintegrará el inventario vendido"
            + " y la venta quedará marcada como anulada en el historial."
            + " Esta acción no se puede deshacer. (F9 confirma)";
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9)
        {
            OnConfirm(sender, e);
            e.Handled = true;
        }
    }
}
