using System.Windows;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Post-sale confirmation (P-UX §9.5, JHO-45): number, total, medium of
/// payment and cash change; offers the optional print and "Nueva venta"
/// holds the initial focus. Esc/close = nueva venta. The sale is already
/// committed at this point; nothing here re-runs the cobro.
/// </summary>
public partial class SaleSavedDialog : Window
{
    public SaleSavedDialog(SaleSavedSummary sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        InitializeComponent();

        SaleNumberText.Text = $"V-{sale.SaleNumber:D6}";
        TotalText.Text = $"Total: {MoneyText.FormatCop(sale.TotalCop)}";
        PaymentText.Text = sale.PaymentMethod == "CASH" ? "Medio de pago: Efectivo" : "Medio de pago: Transferencia";
        CashText.Text = sale.PaymentMethod == "CASH" && sale.ReceivedCop is not null
            ? $"Recibido: {MoneyText.FormatCop(sale.ReceivedCop.Value)}   Cambio: {MoneyText.FormatCop(sale.ChangeCop ?? 0)}"
            : string.Empty;
    }

    /// <summary>True when the operator asked to print the ticket.</summary>
    public bool PrintRequested { get; private set; }

    private void OnNewSale(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        PrintRequested = true;
        DialogResult = true;
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
