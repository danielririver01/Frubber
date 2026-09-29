using System.Windows;
using System.Windows.Controls;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Login window. Composed by <see cref="App"/> with its ViewModel; view
/// concerns (password box, load timing) stay in code-behind.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await viewModel.LoadUsersAsync();
    }

    private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        viewModel.Pin = PinBox.Password;
    }
}
