using System.Windows;
using System.Windows.Controls;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// First-start wizard window. Composed by <see cref="App"/>.
/// </summary>
public partial class FirstStartWindow : Window
{
    private readonly FirstStartViewModel viewModel;

    public FirstStartWindow(FirstStartViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        viewModel.Pin = PinBox.Password;
    }

    private void ConfirmBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        viewModel.ConfirmPin = ConfirmBox.Password;
    }
}
