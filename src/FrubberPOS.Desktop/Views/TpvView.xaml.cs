using System.Windows.Input;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>TPV draft sale (JHO-40): F2 focuses the search (P-UX-011).</summary>
public partial class TpvView : System.Windows.Controls.UserControl
{
    private readonly TpvViewModel viewModel;

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

        base.OnPreviewKeyDown(e);
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await viewModel.SearchAsync();
        SearchBox.Focus();
    }
}
