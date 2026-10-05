using System.Windows;
using System.Windows.Input;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Login window (P-UX §6.5): thin host for <see cref="LoginCardView"/> over the
/// gradient backdrop. Keyboard input is forwarded to the card (which owns the
/// phase-aware routing); the card reports "Esc closes the app" via
/// CloseRequested so lock mode can suppress it.
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
        // Si el VM ya tiene usuarios (p. ej. preview/tests) no recargar:
        // recargar vaciaría la lista y borraría la selección actual.
        if (viewModel.Users.Count == 0)
        {
            await viewModel.LoadUsersAsync();
        }

        await viewModel.LoadTrialAsync();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => Card.HandleKey(e);

    private void OnCardCloseRequested(object? sender, EventArgs e) => Close();
}
