using System.Windows;
using System.Windows.Controls;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>User create/edit dialog (JHO-26, §14.2). View-only glue.</summary>
public partial class UserEditorDialog : Window
{
    private readonly UserEditorViewModel viewModel;

    public UserEditorDialog(UserEditorViewModel viewModel, UserDetailDto? existing)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Load(existing);
        InitializeComponent();
        viewModel.RequestClose += (_, saved) =>
        {
            DialogResult = saved;
            Close();
        };
        Loaded += (_, _) => NameBox.Focus();
    }

    private void PinBox_PasswordChanged(object sender, RoutedEventArgs e) => viewModel.Pin = PinBox.Password;

    private void ConfirmBox_PasswordChanged(object sender, RoutedEventArgs e) => viewModel.ConfirmPin = ConfirmBox.Password;

    private void OnCancel(object sender, RoutedEventArgs e) => viewModel.Cancel();

    private void OnDragMove(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
