using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// TPV draft-sale screen: search box + results + draft lines (JHO-40,
/// P-UX-002/003/006). F2 focuses the search (wire in the view); a
/// canceled or superseded search never overwrites fresher results
/// (token-based staleness guard). Only active products surface.
/// </summary>
public sealed class TpvViewModel : INotifyPropertyChanged
{
    private readonly SearchProductsForSaleUseCase search;
    private readonly DraftSale draft = new();
    private readonly ObservableCollection<TpvSearchItemViewModel> results = [];
    private readonly ObservableCollection<DraftSaleLine> lines = [];

    private string searchText = string.Empty;
    private string errorMessage = string.Empty;
    private CancellationTokenSource? searchCts;
    private TpvSearchItemViewModel? selectedResult;

    public TpvViewModel(SearchProductsForSaleUseCase search)
    {
        ArgumentNullException.ThrowIfNull(search);
        this.search = search;

        SearchCommand = new AsyncCommand(_ => SearchAsync());
        AddSelectedCommand = new AsyncCommand(_ => AddSelectedAsync(), () => SelectedResult is not null);
        ClearDraftCommand = new AsyncCommand(_ => ClearDraft()); // confirmación: JHO-42
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fired to ask the view to focus + select the search box (F2).</summary>
    public event EventHandler? FocusSearchRequested;

    public DraftSale Draft => draft;

    public ObservableCollection<TpvSearchItemViewModel> Results => results;

    public ObservableCollection<DraftSaleLine> Lines => lines;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                _ = SearchAsync();
            }
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public TpvSearchItemViewModel? SelectedResult
    {
        get => selectedResult;
        set
        {
            if (SetProperty(ref selectedResult, value))
            {
                AddSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public long SubtotalCop => draft.SubtotalCop;

    public string SubtotalText => MoneyText.FormatCop(draft.SubtotalCop);

    public AsyncCommand SearchCommand { get; }

    public AsyncCommand AddSelectedCommand { get; }

    public AsyncCommand ClearDraftCommand { get; }

    public void RequestFocusSearch() => FocusSearchRequested?.Invoke(this, EventArgs.Empty);

    public async Task SearchAsync()
    {
        var previous = Interlocked.Exchange(ref searchCts, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();
        var cts = searchCts;

        try
        {
            var result = await search.ExecuteAsync(
                new SearchForSaleRequest(SearchText), cts!.Token);

            // Una búsqueda cancelada o reemplazada no sobrescribe resultados
            // más recientes (criterio JHO-40).
            if (!ReferenceEquals(cts, searchCts) || cts.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            ErrorMessage = string.Empty;
            results.Clear();
            foreach (var item in result.Value.Items)
            {
                results.Add(TpvSearchItemViewModel.From(item));
            }
        }
        catch (OperationCanceledException)
        {
            // Búsqueda cancelada: no tocar los resultados actuales.
        }
    }

    public Task AddSelectedAsync()
    {
        var item = SelectedResult;
        if (item is null)
        {
            return Task.CompletedTask;
        }

        // JHO-42 abre el diálogo de cantidad; mientras, UNIT entra x1 y el
        // usuario edita cantidad para kg/lb (también en JHO-42).
        var added = draft.AddOrAccumulate(item.Source, 1_000);
        if (!added.IsSuccess)
        {
            ErrorMessage = added.Error!.Message;
            return Task.CompletedTask;
        }

        ErrorMessage = string.Empty;
        SyncLines();
        return Task.CompletedTask;
    }

    public Task ClearDraft()
    {
        draft.Clear();
        SyncLines();
        return Task.CompletedTask;
    }

    private void SyncLines()
    {
        lines.Clear();
        foreach (var line in draft.Lines)
        {
            lines.Add(line);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubtotalText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubtotalCop)));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>Row of the TPV search result list (JHO-40): PLU, name, unit, price, stock.</summary>
public sealed record TpvSearchItemViewModel(
    ProductDto Source,
    string Plu,
    string Name,
    string UnitCode,
    string PriceText,
    string StockText)
{
    public static TpvSearchItemViewModel From(TpvSearchItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var unit = Domain.Products.ProductUnits.Parse(dto.UnitCode);
        return new TpvSearchItemViewModel(
            new ProductDto(dto.ProductId, dto.Plu, dto.Name, unit, dto.PriceCop, dto.StockMilli, true, false, false),
            dto.Plu,
            dto.Name,
            dto.UnitCode,
            MoneyText.FormatCop(dto.PriceCop),
            QuantityText.Format(dto.StockMilli, unit) + " " + dto.UnitCode);
    }
}
