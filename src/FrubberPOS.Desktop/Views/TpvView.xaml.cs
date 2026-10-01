using System.Windows.Input;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// TPV draft sale (JHO-40, JHO-44): F2 focuses the search, F4 edits the
/// selected quantity, Supr removes it, F6 opens the discount, F8 opens
/// checkout (P-UX-011); Enter in the search box adds the first result.
/// Cash state refreshes on every load so the P-UX-010 warning stays true
/// after visiting the cash screen (F10).
/// </summary>
public partial class TpvView : System.Windows.Controls.UserControl
{
    private readonly TpvViewModel viewModel;
    private bool loadedOnce;

    public TpvView(TpvViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        viewModel.FocusSearchRequested += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && SearchBox.IsKeyboardFocusWithin)
        {
            if (viewModel.Results.Count > 0)
            {
                viewModel.SelectedResult = viewModel.Results[0];
                _ = viewModel.AddSelectedAsync();
            }

            e.Handled = true;
            return;
        }

        // Supr removes the selected line, but never while typing a search.
        if (e.Key == Key.Delete && !SearchBox.IsKeyboardFocusWithin)
        {
            viewModel.RemoveLineCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F4)
        {
            viewModel.EditQuantityCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6)
        {
            viewModel.OpenDiscountCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F8)
        {
            viewModel.OpenCheckoutCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await viewModel.RefreshCashStateAsync();
        if (loadedOnce)
        {
            return;
        }

        loadedOnce = true;
        await viewModel.SearchAsync();
        SearchBox.Focus();
    }
}
