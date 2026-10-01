using System.Windows.Controls;
using System.Windows.Input;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// Administrative history page (JHO-46, P-UX §13.1/13.2): Enter in a
/// date box searches; double-click opens the read-only detail. The view
/// holds no business rules.
/// </summary>
public partial class SalesHistoryView : UserControl
{
    public SalesHistoryView(SalesHistoryViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        InitializeComponent();
    }

    private SalesHistoryViewModel ViewModel => (SalesHistoryViewModel)DataContext;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter && (FromBox.IsKeyboardFocusWithin || ToBox.IsKeyboardFocusWithin))
        {
            ViewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.OpenDetailCommand.CanExecute(null))
        {
            ViewModel.OpenDetailCommand.Execute(null);
        }
    }
}
