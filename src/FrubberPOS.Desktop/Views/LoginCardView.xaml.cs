using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Login/lock card (JHO-64): renders the owner's prototype (lg-*) in both the
/// startup login window and the secured-session lock overlay. The host owns
/// the chrome (background, window) and forwards keyboard input through
/// <see cref="HandleKey"/>; Esc closing the app is reported via
/// <see cref="CloseRequested"/>.
/// </summary>
public partial class LoginCardView : UserControl
{
    /// <summary>
    /// Lock variant of the card (secured session): hides the license pill and
    /// the 0–9 hint, and binds the host Escape behavior. Set by the host
    /// (usually bound to <see cref="LoginViewModel.IsLockMode"/>).
    /// </summary>
    public static readonly DependencyProperty IsLockModeProperty = DependencyProperty.Register(
        nameof(IsLockMode),
        typeof(bool),
        typeof(LoginCardView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private LoginViewModel? observedViewModel;

    public LoginCardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public event EventHandler? CloseRequested;

    public bool IsLockMode
    {
        get => (bool)GetValue(IsLockModeProperty);
        set => SetValue(IsLockModeProperty, value);
    }

    /// <summary>
    /// Keyboard routing for the whole card (host forwards its own
    /// PreviewKeyDown here). Idempotent: a second call for an already handled
    /// event is a no-op, so window-level and card-level forwarding can coexist.
    /// </summary>
    public void HandleKey(KeyEventArgs e)
    {
        if (e.Handled || DataContext is not LoginViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (viewModel.HandleEscape() == LoginEscapeAction.CloseApp)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

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
        else if (e.Key == Key.Down && viewModel.IsUserPhase)
        {
            viewModel.MoveSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && viewModel.IsUserPhase)
        {
            viewModel.MoveSelection(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            viewModel.HandleEnter();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => HandleKey(e);

    private void OnLoaded(object sender, RoutedEventArgs e) => FocusPhaseTarget();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        observedViewModel = DataContext as LoginViewModel;
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        FocusPhaseTarget();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.IsPinPhase))
        {
            FocusPhaseTarget();
        }
    }

    /// <summary>El foco sigue la fase: lista en usuarios, columna PIN en PIN.</summary>
    private void FocusPhaseTarget()
    {
        Dispatcher.BeginInvoke(new Action(FocusPhaseTargetNow), DispatcherPriority.Input);
    }

    private void FocusPhaseTargetNow()
    {
        if (DataContext is not LoginViewModel viewModel)
        {
            return;
        }

        var target = viewModel.IsPinPhase ? (UIElement)PinColumn : UsersList;
        _ = target.Focus();
    }

    /// <summary>
    /// Clic en una fila: selecciona y pasa al PIN (prototipo lg-2). El recorrido
    /// evita elementos de contenido (runs) que no son visuales.
    /// </summary>
    private void OnUsersListMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not LoginViewModel viewModel)
        {
            return;
        }

        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is ListBoxItem item)
            {
                if (item.Content is ActiveUserDto user)
                {
                    viewModel.SelectAndGoToPin(user);
                    e.Handled = true;
                }

                return;
            }

            current = ParentOf(current);
        }
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or Visual3D
            ? VisualTreeHelper.GetParent(node)
            : node is FrameworkContentElement content
                ? content.Parent
                : null;
}
