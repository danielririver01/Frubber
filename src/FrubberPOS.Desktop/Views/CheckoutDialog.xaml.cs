using System.Windows;
using System.Windows.Input;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Checkout modal (JHO-41, rediseño JHO-67): single-column layout per
/// frubber-pos.html. The ViewModel owns the rules (confirm once, guards
/// while confirming); the code-behind only routes keys and clicks:
/// N1 Enter/F9 in the amount fields, N2 initial focus by discount/method,
/// N3 Esc/✕/outside-click all go through <see cref="CheckoutViewModel.Cancel"/>.
/// </summary>
public partial class CheckoutDialog : Window
{
    private readonly CheckoutViewModel viewModel;

    public CheckoutDialog(
        CheckoutViewModel viewModel,
        long subtotalCop,
        long discountCop,
        int itemCount = 0,
        bool discountMode = false)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Load(subtotalCop, discountCop, itemCount, revealDiscountEditor: discountMode);
        InitializeComponent();
        viewModel.RequestClose += (_, confirmed) =>
        {
            DialogResult = confirmed;
            Close();
        };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // N2 (JHO-67): initial focus follows F6 / payment method / cash.
        if (viewModel.IsDiscountEditorVisible)
        {
            DiscountBox.Focus();
        }
        else if (viewModel.IsTransfer)
        {
            ConfirmButton.Focus();
        }
        else
        {
            ReceivedBox.Focus();
        }
    }

    private void OnAmountPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // N1 (JHO-67): Enter/F9 inside the amount fields confirm directly;
        // the VM guard makes a repeated press a no-op (B2).
        if (e.Key is Key.Enter or Key.F9)
        {
            e.Handled = true;
            viewModel.Confirm();
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9)
        {
            e.Handled = true;
            viewModel.Confirm();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            viewModel.Cancel();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        viewModel.Cancel();
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        // N3 (JHO-67): outside click cancels like the prototype overlay;
        // Cancel is a no-op while IsConfirming.
        if (e.ChangedButton == MouseButton.Left && ReferenceEquals(e.OriginalSource, this))
        {
            viewModel.Cancel();
        }
    }

    private void OnRevealDiscount(object sender, RoutedEventArgs e)
    {
        viewModel.RevealDiscountEditor();
        DiscountBox.Focus();
    }

    private void OnQuickAmount(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            viewModel.ApplyQuickAmount(element.Tag as string);
        }
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
