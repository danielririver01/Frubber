using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.Common;
using QtyFormat = FrubberPOS.Desktop.Common.QuantityText;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// TPV draft-sale screen: search box + results + draft lines (JHO-40,
/// P-UX-002/003/006). F2 focuses the search (wire in the view); a
/// canceled or superseded search never overwrites fresher results
/// (token-based staleness guard). Only active products surface.
/// Quantities: UNIT adds one unit; KG/LB open the dialog (P-UX-009).
/// Repeat accumulates in one line (re-selected, §8.5); F4 edits, Supr
/// removes, and an empty clear is direct while a non-empty one asks
/// "¿Descartar esta venta?" (Esc never discards, P-UX-013). F6 opens
/// discount, F8 checkout, F9 confirms and Esc cancels (JHO-41).
/// Checkout requires an open cash session with a persistent warning
/// (P-UX-010, JHO-44); drafting, discounting and editing stay free.
/// After a sale commits, the confirmation (P-UX §9.5) offers the optional
/// ticket print (JHO-45): the ticket is built from the historical
/// snapshots and a print failure never touches the saved sale (P-UX §9.6,
/// RN-IMP-001/005).
/// Rediseño R2 (JHO-67): floating confirmation toast (M4), header
/// counters + empty states (M6), no-cash banner link to open the cash
/// drawer (M5) and the prototype discount display −$ X (pct%).
/// </summary>
public sealed class TpvViewModel : INotifyPropertyChanged
{
    /// <summary>Persistent P-UX-010 notice while no cash session is open.</summary>
    public const string NoCashWarningText =
        "Sin caja abierta: puede preparar la venta, pero no podrá cobrarla. Presione F10 para abrir la caja.";

    private readonly SearchProductsForSaleUseCase search;
    private readonly FinalizeSaleUseCase finalizeSale;
    private readonly GetOpenCashSessionUseCase getOpenCash;
    private readonly GetTicketForSaleUseCase getTicketForSale;
    private readonly ITicketPrinter ticketPrinter;
    private readonly INavigationService navigation;
    private readonly DraftSale draft = new();
    private readonly ObservableCollection<TpvSearchItemViewModel> results = [];
    private readonly ObservableCollection<TpvLineItemViewModel> lines = [];

    private string searchText = string.Empty;
    private string errorMessage = string.Empty;
    private CancellationTokenSource? searchCts;
    private CancellationTokenSource? toastCts;
    private string toastText = string.Empty;
    private bool toastVisible;
    private TpvSearchItemViewModel? selectedResult;
    private TpvLineItemViewModel? selectedLine;
    private bool? cashOpen;
    private bool submitting;
    private bool isFinalizing;
    private long? pendingPrintSaleNumber;

    public TpvViewModel(
        SearchProductsForSaleUseCase search,
        FinalizeSaleUseCase finalizeSale,
        GetOpenCashSessionUseCase getOpenCash,
        GetTicketForSaleUseCase getTicketForSale,
        ITicketPrinter ticketPrinter,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(finalizeSale);
        ArgumentNullException.ThrowIfNull(getOpenCash);
        ArgumentNullException.ThrowIfNull(getTicketForSale);
        ArgumentNullException.ThrowIfNull(ticketPrinter);
        ArgumentNullException.ThrowIfNull(navigation);
        this.search = search;
        this.finalizeSale = finalizeSale;
        this.getOpenCash = getOpenCash;
        this.getTicketForSale = getTicketForSale;
        this.ticketPrinter = ticketPrinter;
        this.navigation = navigation;

        SearchCommand = new AsyncCommand(_ => SearchAsync());
        AddSelectedCommand = new AsyncCommand(
            _ => AddSelectedAsync(), () => SelectedResult is not null || results.Count > 0);
        EditQuantityCommand = new AsyncCommand(_ => EditQuantityAsync(), () => SelectedLine is not null);
        RemoveLineCommand = new AsyncCommand(_ => RemoveSelectedLine(), () => SelectedLine is not null);
        ClearDraftCommand = new AsyncCommand(_ => ClearDraftWithProtectionAsync());
        OpenDiscountCommand = new AsyncCommand(_ => OpenDiscountAsync(), () => !draft.IsEmpty);
        OpenCheckoutCommand = new AsyncCommand(_ => OpenCheckoutAsync(), () => !draft.IsEmpty && IsCashOpen && !IsFinalizing);
        OpenCashCommand = new RelayCommand(() => OpenCashRequested?.Invoke(this, EventArgs.Empty));
        RemoveDiscountCommand = new RelayCommand(RemoveDiscount, () => draft.DiscountCop > 0);
        RetryPrintCommand = new AsyncCommand(_ => RetryPrintAsync(), () => pendingPrintSaleNumber is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fired to ask the view to focus + select the search box (F2).</summary>
    public event EventHandler? FocusSearchRequested;

    /// <summary>Fired when the P-UX-010 banner link asks the shell to open the cash screen (M5).</summary>
    public event EventHandler? OpenCashRequested;

    public DraftSale Draft => draft;

    public ObservableCollection<TpvSearchItemViewModel> Results => results;

    public ObservableCollection<TpvLineItemViewModel> Lines => lines;

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
        private set
        {
            if (SetProperty(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasErrorMessage));
            }
        }
    }

    /// <summary>B2: banner visible while any error message is set.</summary>
    public bool HasErrorMessage => !string.IsNullOrEmpty(errorMessage);

    /// <summary>
    /// B2 (JHO-67): true while the confirmed sale is committing; Cobrar is
    /// disabled and reads "Registrando la venta…" so the operator never
    /// charges twice (RN-VEN-002).
    /// </summary>
    public bool IsFinalizing
    {
        get => isFinalizing;
        private set
        {
            if (SetProperty(ref isFinalizing, value))
            {
                OnPropertyChanged(nameof(CanCharge));
                OnPropertyChanged(nameof(ChargeText));
                OpenCheckoutCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>B2: Cobrar label while finalizing.</summary>
    public string ChargeText => IsFinalizing ? "Registrando la venta…" : "Cobrar";

    /// <summary>B2: retry appears only while a saved ticket failed to print (RN-IMP-005).</summary>
    public bool RetryPrintVisible => pendingPrintSaleNumber is not null;

    /// <summary>True only after a refresh confirmed an open session; null before the first check.</summary>
    public bool IsCashOpen => cashOpen == true;

    /// <summary>P-UX-010: persistent warning while no cash session is open.</summary>
    public bool CashWarningVisible => cashOpen == false;

    public string CashWarningText => cashOpen == false ? NoCashWarningText : string.Empty;

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

    public TpvLineItemViewModel? SelectedLine
    {
        get => selectedLine;
        set
        {
            if (SetProperty(ref selectedLine, value))
            {
                EditQuantityCommand.RaiseCanExecuteChanged();
                RemoveLineCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public long SubtotalCop => draft.SubtotalCop;

    public string SubtotalText => MoneyText.FormatCop(draft.SubtotalCop);

    public long DiscountCop => draft.DiscountCop;

    public string DiscountText => MoneyText.FormatCop(draft.DiscountCop);

    public long TotalCop => draft.TotalCop;

    public string TotalText => MoneyText.FormatCop(draft.TotalCop);

    /// <summary>Header counter of RESULTADOS (prototype: "N productos").</summary>
    public string ResultsCountText => results.Count == 1 ? "1 producto" : $"{results.Count} productos";

    public bool ResultsEmptyVisible => results.Count == 0;

    public bool ResultsTableVisible => results.Count > 0;

    /// <summary>Header counter of VENTA EN PREPARACIÓN (prototype: "N ítems").</summary>
    public string LineCountText => lines.Count == 1 ? "1 ítem" : $"{lines.Count} ítems";

    public bool TicketEmptyVisible => lines.Count == 0;

    public bool TicketTableVisible => lines.Count > 0;

    /// <summary>Cobrar only with draft lines and an open cash session (P-UX-010).</summary>
    public bool CanCharge => !draft.IsEmpty && IsCashOpen;

    public bool DiscountAppliedVisible => draft.DiscountCop > 0;

    /// <summary>Applied discount percent rounded down (display only, M6).</summary>
    public string DiscountPercentText
    {
        get
        {
            if (draft.DiscountCop <= 0 || draft.SubtotalCop <= 0)
            {
                return "0%";
            }

            return $"{draft.DiscountCop * 100 / draft.SubtotalCop}%";
        }
    }

    /// <summary>Prototype discount line: "−$ X (pct%)" while applied, "−$ 0" otherwise.</summary>
    public string DiscountDisplayText
    {
        get
        {
            var amount = "−" + MoneyText.FormatCop(draft.DiscountCop);
            return draft.DiscountCop > 0 && draft.SubtotalCop > 0
                ? $"{amount} ({draft.DiscountCop * 100 / draft.SubtotalCop}%)"
                : amount;
        }
    }

    /// <summary>Toast lifetime for the floating confirmation pill (M4).</summary>
    public static readonly TimeSpan ToastDuration = TimeSpan.FromMilliseconds(2400);

    public string ToastText
    {
        get => toastText;
        private set => SetProperty(ref toastText, value);
    }

    public bool ToastVisible
    {
        get => toastVisible;
        private set => SetProperty(ref toastVisible, value);
    }

    public AsyncCommand SearchCommand { get; }

    public AsyncCommand AddSelectedCommand { get; }

    public AsyncCommand EditQuantityCommand { get; }

    public AsyncCommand RemoveLineCommand { get; }

    public AsyncCommand ClearDraftCommand { get; }

    public AsyncCommand OpenDiscountCommand { get; }

    public AsyncCommand OpenCheckoutCommand { get; }

    public RelayCommand OpenCashCommand { get; }

    public RelayCommand RemoveDiscountCommand { get; }

    /// <summary>B2: reprints the last saved ticket without re-running the cobro.</summary>
    public AsyncCommand RetryPrintCommand { get; }

    public CheckoutResultDto? LastConfirmedCheckout { get; private set; }

    public long? LastSaleNumber { get; private set; }

    public void RequestFocusSearch() => FocusSearchRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Re-reads the open cash session (P-UX-010, JHO-44) and refreshes the
    /// checkout gate + warning. Called on every view load and after a sale.
    /// </summary>
    public async Task RefreshCashStateAsync()
    {
        var result = await getOpenCash.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None);
        cashOpen = result.IsSuccess && result.Value.Session is not null;
        OnPropertyChanged(nameof(IsCashOpen));
        OnPropertyChanged(nameof(CashWarningVisible));
        OnPropertyChanged(nameof(CashWarningText));
        OnPropertyChanged(nameof(CanCharge));
        OpenCheckoutCommand.RaiseCanExecuteChanged();
    }

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

            // JHO-60: la pantalla abre con todo el catálogo activo (consulta
            // vacía = todo) y el primer resultado listo para Enter.
            SelectedResult = results.FirstOrDefault();
            AddSelectedCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ResultsCountText));
            OnPropertyChanged(nameof(ResultsEmptyVisible));
            OnPropertyChanged(nameof(ResultsTableVisible));
        }
        catch (OperationCanceledException)
        {
            // Superseded search: leave current results untouched.
        }
    }

    public Task AddSelectedAsync()
    {
        // JHO-74: la selección puede quedar vacía u obsoleta tras una
        // búsqueda reciente (fila única recién clickeada); en ese caso se
        // agrega la primera fila de los resultados vigentes.
        var item = SelectedResult is { } current && results.Contains(current)
            ? current
            : results.FirstOrDefault();
        if (item is null)
        {
            return Task.CompletedTask;
        }

        var quantity = item.Source.Unit == Domain.Products.ProductUnit.Unit
            ? 1_000L
            : navigation.AskQuantity(item.Source);
        if (quantity is null)
        {
            return Task.CompletedTask;
        }

        var added = draft.AddOrAccumulate(item.Source, quantity.Value);
        if (!added.IsSuccess)
        {
            ErrorMessage = added.Error!.Message;
            return Task.CompletedTask;
        }

        ErrorMessage = string.Empty;

        // §8.5: the table re-selects the line just added/accumulated.
        SyncLines(item.Source.ProductId);
        ShowToast($"{item.Source.Name} agregado");
        return Task.CompletedTask;
    }

    public Task EditQuantityAsync()
    {
        var selected = SelectedLine;
        if (selected is null)
        {
            return Task.CompletedTask;
        }

        var line = selected.Line;
        var quantity = navigation.AskQuantity(new ProductDto(
            line.ProductId, line.Plu, line.Name, line.Unit, line.UnitPriceCop, 0, true, true));
        if (quantity is null)
        {
            return Task.CompletedTask;
        }

        var result = draft.RemoveOrSetQuantity(line.ProductId, quantity.Value);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return Task.CompletedTask;
        }

        ErrorMessage = string.Empty;
        SyncLines(line.ProductId);
        ShowToast("Cantidad actualizada");
        return Task.CompletedTask;
    }

    public Task RemoveSelectedLine()
    {
        var selected = SelectedLine;
        if (selected is null)
        {
            return Task.CompletedTask;
        }

        var name = selected.Line.Name;
        draft.RemoveLine(selected.Line.ProductId);
        SelectedLine = null;
        ErrorMessage = string.Empty;
        SyncLines();
        ShowToast($"{name} eliminado");
        return Task.CompletedTask;
    }

    public Task ClearDraftWithProtectionAsync()
    {
        if (draft.IsEmpty)
        {
            return Task.CompletedTask;
        }

        if (!navigation.ConfirmDiscardDraft())
        {
            return Task.CompletedTask;
        }

        return ClearDraft();
    }

    public Task ClearDraft()
    {
        draft.Clear();
        SelectedLine = null;
        SyncLines();
        ShowToast("Venta limpiada");
        return Task.CompletedTask;
    }

    public Task OpenDiscountAsync() => HandleCheckoutAsync(finalize: false);

    public Task OpenCheckoutAsync() => HandleCheckoutAsync(finalize: true);

    private async Task HandleCheckoutAsync(bool finalize)
    {
        // Double-submit guard (JHO-39): a second Enter/click while the
        // checkout flow is in flight never reaches the dialog or the
        // finalization — the sale is created at most once (RN-VEN-002).
        if (submitting)
        {
            return;
        }

        if (draft.IsEmpty)
        {
            ErrorMessage = "No hay productos en la venta.";
            return;
        }

        // P-UX-010: only finalization is blocked without an open cash
        // session; discounting stays available. Command gate is the
        // primary guard — this protects direct calls too.
        if (finalize && !IsCashOpen)
        {
            ErrorMessage = NoCashWarningText;
            return;
        }

        submitting = true;
        try
        {
            await RunCheckoutAsync(finalize);
        }
        finally
        {
            submitting = false;
        }
    }

    private async Task RunCheckoutAsync(bool finalize)
    {
        var result = navigation.ShowCheckout(
            draft.SubtotalCop, draft.DiscountCop, draft.Lines.Count, discountMode: !finalize);
        if (result is null || !result.Confirmed)
        {
            return;
        }

        var discounted = draft.SetDiscount(result.DiscountCop);
        if (!discounted.IsSuccess)
        {
            ErrorMessage = discounted.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        SyncLines();

        if (!finalize)
        {
            if (draft.DiscountCop > 0)
            {
                ShowToast($"Descuento del {DiscountPercentText}");
            }

            return;
        }

        // B2: from the confirmed cobro until the sale is fully settled the
        // Cobrar button reads "Registrando la venta…" and stays disabled.
        IsFinalizing = true;
        try
        {
            var finalized = await finalizeSale.ExecuteAsync(
                new FinalizeSaleRequest(
                    draft.Lines.ToList(),
                    result.DiscountCop,
                    result.PaymentMethod,
                    result.PaymentMethod == "CASH" ? result.ReceivedCop : (long?)null),
                CancellationToken.None);

            if (!finalized.IsSuccess)
            {
                ErrorMessage = finalized.Error!.Message;
                return;
            }

            LastConfirmedCheckout = result;
            LastSaleNumber = finalized.Value.SaleNumber;
            draft.Clear();
            SelectedLine = null;
            SyncLines();
            await OfferTicketPrintingAsync(finalized.Value.SaleNumber, result, finalized.Value.TotalCop);
            await RefreshCashStateAsync();

            // B2: the floating pill reinforces the sale right after the
            // confirmation dialog closes (M4).
            ShowToast($"Venta {SaleNumberText(finalized.Value.SaleNumber)} guardada");
        }
        finally
        {
            IsFinalizing = false;
        }
    }

    /// <summary>
    /// Optional ticket printing after a committed sale (JHO-45, P-UX §9.5).
    /// The ticket is composed from the historical data (never the draft)
    /// and a build/print failure only reports "la venta quedó guardada"
    /// (P-UX §9.6) — the cobro is never re-executed to retry (RN-IMP-005).
    /// </summary>
    private async Task OfferTicketPrintingAsync(long saleNumber, CheckoutResultDto checkout, long totalCop)
    {
        var wantsTicket = navigation.ShowSaleSaved(new SaleSavedSummary(
            saleNumber,
            totalCop,
            checkout.PaymentMethod,
            checkout.PaymentMethod == "CASH" ? checkout.ReceivedCop : null,
            checkout.PaymentMethod == "CASH" ? checkout.ChangeCop : null));
        if (!wantsTicket)
        {
            ClearPrintFailure();
            return;
        }

        await PrintTicketAsync(saleNumber);
    }

    /// <summary>
    /// Shared print attempt (B2): success and picker-cancel clear the
    /// banner; a build/print failure arms <see cref="RetryPrintCommand"/>
    /// without ever touching the saved sale (RN-IMP-005).
    /// </summary>
    private async Task PrintTicketAsync(long saleNumber)
    {
        var ticket = await getTicketForSale.ExecuteAsync(new GetTicketForSaleRequest(saleNumber), CancellationToken.None);
        if (!ticket.IsSuccess)
        {
            SetPrintFailure(saleNumber);
            return;
        }

        var printerName = navigation.AskPrinter(ticket.Value.ConfiguredPrinterName);
        if (printerName is null)
        {
            ClearPrintFailure();
            return;
        }

        if (ticketPrinter.Print(ticket.Value, printerName) is not null)
        {
            SetPrintFailure(saleNumber);
            return;
        }

        ClearPrintFailure();
        ShowToast("Ticket impreso");
    }

    /// <summary>B2: arms the banner + retry for the given saved sale.</summary>
    private void SetPrintFailure(long saleNumber)
    {
        pendingPrintSaleNumber = saleNumber;
        ErrorMessage = PrintFailureText(SaleNumberText(saleNumber));
        OnPropertyChanged(nameof(RetryPrintVisible));
        RetryPrintCommand.RaiseCanExecuteChanged();
    }

    private void ClearPrintFailure()
    {
        pendingPrintSaleNumber = null;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(RetryPrintVisible));
        RetryPrintCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// B2: retries the print of the saved sale that failed (RN-IMP-005) —
    /// the cobro itself is never re-executed.
    /// </summary>
    private Task RetryPrintAsync()
    {
        if (pendingPrintSaleNumber is not { } saleNumber)
        {
            return Task.CompletedTask;
        }

        return PrintTicketAsync(saleNumber);
    }

    private static string SaleNumberText(long saleNumber) => $"V-{saleNumber:D6}";

    /// <summary>P-UX §9.6: the print failure never blames the sale.</summary>
    private static string PrintFailureText(string saleNumberText) =>
        $"La venta {saleNumberText} quedó guardada, pero el ticket no pudo imprimirse.";

    /// <summary>M6 quick remove of the applied discount without opening F6.</summary>
    private void RemoveDiscount()
    {
        var result = draft.SetDiscount(0);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        SyncLines();
        ShowToast("Descuento retirado");
    }

    /// <summary>
    /// M4: shows the floating confirmation pill for <see cref="ToastDuration"/>
    /// and hides it unless a newer toast replaced it meanwhile. Never takes
    /// keyboard focus (the search keeps it for fast entry).
    /// </summary>
    private void ShowToast(string message)
    {
        ToastText = message;
        ToastVisible = true;
        toastCts?.Cancel();
        toastCts?.Dispose();
        var cts = new CancellationTokenSource();
        toastCts = cts;
        _ = HideToastAsync(cts);
    }

    private async Task HideToastAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(ToastDuration, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ReferenceEquals(toastCts, cts))
        {
            ToastVisible = false;
        }
    }

    private void SyncLines(long? selectProductId = null)
    {
        lines.Clear();
        var number = 0;
        TpvLineItemViewModel? toSelect = null;
        foreach (var line in draft.Lines)
        {
            var item = new TpvLineItemViewModel(++number, line);
            lines.Add(item);
            if (selectProductId is not null && line.ProductId == selectProductId.Value)
            {
                toSelect = item;
            }
        }

        SelectedLine = toSelect;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubtotalText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubtotalCop)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscountText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineCountText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TicketEmptyVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TicketTableVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscountAppliedVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscountPercentText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscountDisplayText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCharge)));
        ClearDraftCommand.RaiseCanExecuteChanged();
        OpenDiscountCommand.RaiseCanExecuteChanged();
        OpenCheckoutCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
    /// <summary>Initial of the product shown in the mint avatar cell (proto).</summary>
    public string FirstLetter => Name.Length == 0 ? "?" : char.ToUpperInvariant(Name[0]).ToString();

    /// <summary>
    /// JHO-67: umbral fijo de stock bajo aprobado por el dueño — la DB no
    /// trae columna de mínimo por producto (001_Initial.sql es inmutable):
    /// 0 &lt; stock ≤ 5 KG/LB/UND ⇒ "bajo"; stock = 0 ⇒ "agotado".
    /// </summary>
    public const long LowStockThresholdMilli = 5_000;

    public long StockMilli => Source.StockMilli;

    public bool IsOutOfStock => StockMilli <= 0;

    public bool IsLowStock => StockMilli > 0 && StockMilli <= LowStockThresholdMilli;

    /// <summary>Text of the amber/red badge next to the stock, or empty when healthy.</summary>
    public string StockStatusText => IsOutOfStock ? "agotado" : IsLowStock ? "bajo" : string.Empty;

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

/// <summary>
/// Draft-line row for the TPV table (§8.6, JHO-44): line number, product,
/// quantity formatted with its unit, unit price and line total — all text,
/// never raw thousandths (ADR-009 shows money already formatted).
/// </summary>
public sealed record TpvLineItemViewModel(int Number, DraftSaleLine Line)
{
    public long ProductId => Line.ProductId;

    public long QuantityMilli => Line.QuantityMilli;

    public long LineTotalCop => Line.LineTotalCop;

    public string Name => Line.Name;

    public string QuantityText =>
        QtyFormat.Format(Line.QuantityMilli, Line.Unit) + " " + Domain.Products.ProductUnits.ToCode(Line.Unit);

    public string PriceText => MoneyText.FormatCop(Line.UnitPriceCop);

    /// <summary>" /KG" or " /LB" after the unit price of weighed products (proto .per).</summary>
    public string UnitPriceSuffix =>
        Line.Unit is Domain.Products.ProductUnit.Kg or Domain.Products.ProductUnit.Lb
            ? " /" + Domain.Products.ProductUnits.ToCode(Line.Unit)
            : string.Empty;

    public string TotalText => MoneyText.FormatCop(Line.LineTotalCop);
}
