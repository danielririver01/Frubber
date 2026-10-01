using System.Windows;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Printer picker (JHO-45, P-UX §15.2): lists the installed Windows
/// printers and preselects the configured one; Cancel (or Esc) means no
/// printing — printing is always optional (RN-IMP-005). When the team has
/// no printer installed, printing stays disabled.
/// </summary>
public partial class PrintTicketDialog : Window
{
    public PrintTicketDialog(IReadOnlyList<string> installedPrinters, string? configuredPrinterName)
    {
        ArgumentNullException.ThrowIfNull(installedPrinters);
        InitializeComponent();

        foreach (var printer in installedPrinters)
        {
            PrinterCombo.Items.Add(printer);
        }

        if (PrinterCombo.Items.Count == 0)
        {
            NoPrintersText.Visibility = Visibility.Visible;
            PrintButton.IsEnabled = false;
            return;
        }

        var configuredIndex = -1;
        if (configuredPrinterName is not null)
        {
            for (var index = 0; index < installedPrinters.Count; index++)
            {
                if (string.Equals(installedPrinters[index], configuredPrinterName, StringComparison.Ordinal))
                {
                    configuredIndex = index;
                    break;
                }
            }
        }

        PrinterCombo.SelectedIndex = configuredIndex >= 0 ? configuredIndex : 0;
    }

    /// <summary>Chosen printer; valid only when the dialog result is true.</summary>
    public string? SelectedPrinterName { get; private set; }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        SelectedPrinterName = PrinterCombo.SelectedItem as string;
        DialogResult = SelectedPrinterName is not null;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
