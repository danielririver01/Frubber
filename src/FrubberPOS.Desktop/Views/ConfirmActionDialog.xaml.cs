using System.Windows;
using System.Windows.Input;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Destructive-action confirmation (JHO-50, P-UX-012/013): the button
/// names the action and the message names the object and consequence —
/// never a bare "Sí". F9 confirms, Esc cancels.
/// </summary>
public partial class ConfirmActionDialog : Window
{
    public ConfirmActionDialog(string title, string message, string confirmActionText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmActionText);
        InitializeComponent();
        Title = $"Frubber POS — {title}";
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmActionText;
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
