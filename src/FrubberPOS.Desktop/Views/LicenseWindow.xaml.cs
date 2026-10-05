using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// License activation dialog (JHO-25, rediseño JHO-64): covers the owner
/// with a dim backdrop like the prototype's licBackdrop, wires the close
/// flow with discard confirmation, Enter/Ctrl+C/Esc shortcuts, focuses the
/// key input shortly after opening and auto-closes two seconds after the
/// success panel. Clipboard access stays here so the ViewModel remains
/// unit-testable.
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
        if (Owner is not null)
        {
            // CenterOwner se calculó con el tamaño inicial (480x640): al
            // redimensionar sobre el owner hay que reposicionar también o el
            // modal queda corrido hacia la derecha, fuera de la ventana (JHO-64).
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Owner.Left;
            Top = Owner.Top;
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        await viewModel.LoadAsync();
        await Task.Delay(180);
        FocusInput();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    // Éxito (lic-success): el prototipo cierra a los 2000 ms
    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LicenseActivationViewModel.ShowSuccess) && viewModel.ShowSuccess)
        {
            await Task.Delay(2000);
            Close();
        }
    }

    private void OnCopyCode(object sender, RoutedEventArgs e)
    {
        CopyMachineCode();
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => RequestClose();

    private void OnBackdropClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        RequestClose();
    }

    private void OnDiscardBackdropClick(object sender, MouseButtonEventArgs e) => viewModel.KeepEditing();

    private void OnKeepEditing(object sender, RoutedEventArgs e)
    {
        viewModel.KeepEditing();
        FocusInput();
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        viewModel.DiscardCode();
        Close();
    }

    // Esc cierra · Enter activa · Ctrl+C copia el código del equipo (lic-*)
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (viewModel.ShowDiscardConfirm)
            {
                viewModel.KeepEditing();
            }
            else
            {
                RequestClose();
            }

            e.Handled = true;
            return;
        }

        // Mini modal de descarte abierto: solo Esc aplica (prototipo lic-*)
        if (viewModel.ShowDiscardConfirm)
        {
            return;
        }

        if (e.Key == Key.Enter && viewModel.CanActivate)
        {
            viewModel.ActivateCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (Keyboard.FocusedElement is TextBox { SelectionLength: > 0 })
            {
                return;
            }

            CopyMachineCode();
            e.Handled = true;
        }
    }

    private void RequestClose()
    {
        if (viewModel.TryRequestClose())
        {
            Close();
        }
    }

    private void CopyMachineCode()
    {
        if (!string.IsNullOrWhiteSpace(viewModel.MachineDisplayCode))
        {
            Clipboard.SetText(viewModel.MachineDisplayCode);
            viewModel.NotifyCopied();
        }
    }

    private void FocusInput()
    {
        LicenseInput.Focus();
        Keyboard.Focus(LicenseInput);
    }
}
