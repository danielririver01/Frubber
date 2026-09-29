using System.Windows;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// License activation dialog (JHO-25): copyable team code, license paste,
/// lifetime status. Composed by <see cref="App"/> with its ViewModel.
/// Clipboard access stays here so the ViewModel remains unit-testable.
/// </summary>
public partial class LicenseWindow : Window
{
    private readonly LicenseActivationViewModel viewModel;

    public LicenseWindow(LicenseActivationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await viewModel.LoadAsync();
    }

    private void OnCopyCode(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(viewModel.MachineDisplayCode))
        {
            Clipboard.SetText(viewModel.MachineDisplayCode);
        }
    }
}
