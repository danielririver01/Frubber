using System.Windows;
using System.Windows.Controls;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>PIN reset dialog (JHO-26). View-only glue for the PasswordBoxes.</summary>
public partial class ResetPinDialog : Window
{
    private readonly ResetPinViewModel viewModel;

    public ResetPinDialog(ResetPinViewModel viewModel, UserDetailDto user)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Load(user);
        InitializeComponent();
        viewModel.RequestClose += (_, saved) =>
        {
            DialogResult = saved;
            Close();
        };
        Loaded += (_, _) => PinBox.Focus();
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
