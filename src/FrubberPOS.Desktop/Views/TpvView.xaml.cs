using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrubberPOS.Desktop.ViewModels;

namespace FrubberPOS.Desktop.Views;

/// <summary>
/// TPV draft sale (JHO-40, JHO-44, JHO-67 B3): F2 focuses the search, F4
/// edits the selected quantity, Supr removes it, F6 opens the discount, F8
/// opens checkout (P-UX-011). Keyboard precedence while typing: Enter adds
/// the highlighted result (first row as fallback), Esc clears the query,
/// arrows move the highlight; Enter on the ticket (cell/grid focus) opens
/// checkout; the per-row ✎/🗑 buttons select their own line first. Cash state
/// refreshes on every load so the P-UX-010 warning stays true after
/// visiting the cash screen (F10).
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

        // Zona de búsqueda (B3): el escribir manda; Enter agrega lo resaltado
        // con respaldo a la primera fila, Esc limpia, ↑↓ mueven el resaltado.
        // JHO-74: Enter también agrega con el foco en una fila de resultados
        // (el clic en el único resultado dejaba la tecla sin efecto).
        if (e.Key == Key.Enter
            && (SearchBox.IsKeyboardFocusWithin || ResultsGrid.IsKeyboardFocusWithin))
        {
            _ = viewModel.AddSelectedAsync();
            e.Handled = true;
            return;
        }

        if (SearchBox.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Escape)
            {
                viewModel.SearchText = string.Empty; // el setter ya relanza la búsqueda
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Up or Key.Down)
            {
                MoveResultSelection(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                return;
            }
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

        // Ticket con foco (B3): Enter abre el cobro, salvo que el foco esté
        // en un botón de fila (✎/🗑), que debe poder activarse con Enter.
        if (e.Key == Key.Enter
            && TicketGrid.IsKeyboardFocusWithin
            && Keyboard.FocusedElement is DataGrid or DataGridCell or DataGridRow)
        {
            viewModel.OpenCheckoutCommand.Execute(null);
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

        base.OnPreviewKeyDown(e);
    }

    private void MoveResultSelection(int delta)
    {
        if (viewModel.Results.Count == 0)
        {
            return;
        }

        var index = viewModel.SelectedResult is { } current ? viewModel.Results.IndexOf(current) : -1;
        index = Math.Clamp(index + delta, 0, viewModel.Results.Count - 1);
        viewModel.SelectedResult = viewModel.Results[index];
    }

    // Select-then-execute (B3/M7): cada botón de fila opera sobre su propia
    // línea aunque el ticket tenga otra seleccionada.
    private void OnEditLineClick(object sender, RoutedEventArgs e) =>
        ExecuteLineAction(sender, viewModel.EditQuantityCommand);

    private void OnRemoveLineClick(object sender, RoutedEventArgs e) =>
        ExecuteLineAction(sender, viewModel.RemoveLineCommand);

    private void ExecuteLineAction(object sender, ICommand command)
    {
        if (sender is Button { CommandParameter: TpvLineItemViewModel line })
        {
            viewModel.SelectedLine = line;
        }

        command.Execute(null);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await viewModel.RefreshCashStateAsync();
        // Refresh results on every visit: products created while the page
        // was cached (single TpvViewModel for the session) must appear.
        await viewModel.SearchAsync();
        if (!loadedOnce)
        {
            loadedOnce = true;
            SearchBox.Focus();
        }
    }
}
