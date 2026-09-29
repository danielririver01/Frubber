using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Login window: user list plus PIN pad (P-UX §6.5). Composed by
/// <see cref="App"/> with its ViewModel; keyboard routing (arrows move in
/// the list natively, digits feed the pad, Enter confirms) stays here.
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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var digit = e.Key switch
        {
            Key.D0 or Key.NumPad0 => "0",
            Key.D1 or Key.NumPad1 => "1",
            Key.D2 or Key.NumPad2 => "2",
            Key.D3 or Key.NumPad3 => "3",
            Key.D4 or Key.NumPad4 => "4",
            Key.D5 or Key.NumPad5 => "5",
            Key.D6 or Key.NumPad6 => "6",
            Key.D7 or Key.NumPad7 => "7",
            Key.D8 or Key.NumPad8 => "8",
            Key.D9 or Key.NumPad9 => "9",
            _ => null
        };

        if (digit is not null)
        {
            viewModel.EnterDigit(digit);
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            viewModel.Backspace();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && viewModel.LoginCommand.CanExecute(null))
        {
            viewModel.LoginCommand.Execute(null);
            e.Handled = true;
        }
    }
}
