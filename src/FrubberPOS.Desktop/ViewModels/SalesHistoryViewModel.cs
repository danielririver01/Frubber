using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Administrative history page (JHO-46/75, P-UX §13.1/13.2): opens with
/// the FULL history (no date filter); Desde/Hasta are an optional
/// real-time filter (both or none, Hasta never before Desde), Enter
/// searches, 50 per page, newest first, Anterior/Siguiente reflect
/// availability and the detail opens per row. Empty state shows a
/// contextual message (no sales vs no sales in range). The list is
/// read-only; voiding happens in the detail.
/// </summary>
public sealed class SalesHistoryViewModel : INotifyPropertyChanged
{
    private readonly SearchSalesHistoryUseCase searchHistory;
    private readonly INavigationService navigation;
    private readonly ObservableCollection<SaleHistoryRowViewModel> items = [];

    private string fromText = string.Empty;
    private string toText = string.Empty;
    private string errorMessage = string.Empty;
    private string summaryText = string.Empty;
    private string emptyTitle = string.Empty;
    private string emptySub = string.Empty;
    private bool emptyVisible;
    private bool rowsVisible;
    private bool emptyCtaVisible;
    private bool filterActive;
    private int page = 1;
    private int totalCount;
    private int searchVersion;
    private SaleHistoryRowViewModel? selectedItem;

    public SalesHistoryViewModel(SearchSalesHistoryUseCase searchHistory, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(searchHistory);
        ArgumentNullException.ThrowIfNull(navigation);
        this.searchHistory = searchHistory;
        this.navigation = navigation;

        SearchCommand = new AsyncCommand(_ => SearchAsync(1));
        PreviousPageCommand = new AsyncCommand(_ => SearchAsync(page - 1), () => page > 1);
        NextPageCommand = new AsyncCommand(_ => SearchAsync(page + 1), () => (long)page * SearchSalesHistoryUseCase.PageSize < totalCount);
        OpenDetailCommand = new AsyncCommand(_ => OpenDetailAsync(), () => SelectedItem is not null);
        ClearFilterCommand = new AsyncCommand(_ => ClearFilterAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SaleHistoryRowViewModel> Items => items;

    public string FromText
    {
        get => fromText;
        set
        {
            if (SetProperty(ref fromText, value))
            {
                OnFilterChanged();
            }
        }
    }

    public string ToText
    {
        get => toText;
        set
        {
            if (SetProperty(ref toText, value))
            {
                OnFilterChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    /// <summary>"Página 1 · 120 ventas" — availability context for Anterior/Siguiente.</summary>
    public string SummaryText
    {
        get => summaryText;
        private set => SetProperty(ref summaryText, value);
    }

    /// <summary>Empty-state visibility (no rows after the search).</summary>
    public bool EmptyVisible
    {
        get => emptyVisible;
        private set => SetProperty(ref emptyVisible, value);
    }

    /// <summary>Inverse of EmptyVisible: the DataGrid only shows when there are rows.</summary>
    public bool RowsVisible
    {
        get => rowsVisible;
        private set => SetProperty(ref rowsVisible, value);
    }

    public string EmptyTitle
    {
        get => emptyTitle;
        private set => SetProperty(ref emptyTitle, value);
    }

    public string EmptySub
    {
        get => emptySub;
        private set => SetProperty(ref emptySub, value);
    }

    /// <summary>"Limpiar fechas" only makes sense while a date filter is active.</summary>
    public bool EmptyCtaVisible
    {
        get => emptyCtaVisible;
        private set => SetProperty(ref emptyCtaVisible, value);
    }

    public int Page => page;

    public int TotalCount => totalCount;

    public SaleHistoryRowViewModel? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (SetProperty(ref selectedItem, value))
            {
                OpenDetailCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncCommand SearchCommand { get; }

    public AsyncCommand PreviousPageCommand { get; }

    public AsyncCommand NextPageCommand { get; }

    public AsyncCommand OpenDetailCommand { get; }

    public AsyncCommand ClearFilterCommand { get; }

    /// <summary>JHO-60/75: la página carga TODO el historial sin pulsar Buscar.</summary>
    public Task InitializeAsync() => SearchAsync(1);

    /// <summary>JHO-75: el rango se aplica en tiempo real al digitar (ambas fechas o ninguna).</summary>
    private void OnFilterChanged()
    {
        var fromEmpty = string.IsNullOrWhiteSpace(FromText);
        var toEmpty = string.IsNullOrWhiteSpace(ToText);
        if (fromEmpty && toEmpty)
        {
            ErrorMessage = string.Empty;
            _ = SearchAsync(1);
            return;
        }

        if (!fromEmpty && !toEmpty
            && TryParseDate(FromText, out _) && TryParseDate(ToText, out _))
        {
            ErrorMessage = string.Empty;
            _ = SearchAsync(1);
            return;
        }

        if (fromEmpty != toEmpty)
        {
            ErrorMessage = "Complete ambas fechas para filtrar o déjelas vacías.";
        }
    }

    public async Task ClearFilterAsync()
    {
        FromText = string.Empty;
        ToText = string.Empty;
        ErrorMessage = string.Empty;
        await SearchAsync(1);
    }

    public async Task SearchAsync(int targetPage = 1)
    {
        var version = ++searchVersion;
        DateOnly? from = null;
        DateOnly? to = null;
        var fromEmpty = string.IsNullOrWhiteSpace(FromText);
        var toEmpty = string.IsNullOrWhiteSpace(ToText);
        if (fromEmpty != toEmpty)
        {
            ErrorMessage = "Complete ambas fechas para filtrar o déjelas vacías.";
            return;
        }

        if (!fromEmpty)
        {
            if (!TryParseDate(FromText, out var fromDate) || !TryParseDate(ToText, out var toDate))
            {
                ErrorMessage = "Ingrese las fechas Desde y Hasta con formato dd/MM/aaaa.";
                return;
            }

            if (toDate < fromDate)
            {
                ErrorMessage = "Hasta no puede ser anterior a Desde.";
                return;
            }

            from = fromDate;
            to = toDate;
        }

        var result = await searchHistory.ExecuteAsync(
            new SearchSalesHistoryRequest(from, to, targetPage), CancellationToken.None);
        if (version != searchVersion)
        {
            return; // una búsqueda más reciente ya sobrescribió esta página (filtro en tiempo real)
        }

        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        page = result.Value.Page;
        totalCount = result.Value.TotalCount;
        filterActive = from is not null;
        items.Clear();
        foreach (var item in result.Value.Items)
        {
            items.Add(SaleHistoryRowViewModel.From(item));
        }

        SelectedItem = items.FirstOrDefault();
        RowsVisible = totalCount > 0;
        EmptyVisible = totalCount == 0;
        EmptyCtaVisible = filterActive;
        EmptyTitle = filterActive ? "No hay ventas en este rango" : "No hay ventas registradas";
        EmptySub = filterActive
            ? "Amplía las fechas o limpia el filtro para ver más ventas."
            : "Las ventas aparecerán aquí después de cada cobro.";
        SummaryText = totalCount == 0
            ? (filterActive ? "Sin ventas en el rango." : "No hay ventas registradas.")
            : $"Página {page} · {totalCount:N0} ventas";
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
    }

    public async Task OpenDetailAsync()
    {
        var selected = SelectedItem;
        if (selected is null)
        {
            return;
        }

        var voided = navigation.ShowSaleDetail(selected.SaleNumber);
        if (voided)
        {
            await SearchAsync(page); // el estado cambió: refrescar la misma página
        }
    }

    private static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(text)
            && DateOnly.TryParseExact(text.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
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

/// <summary>
/// History row (P-UX §13.2/13.5): number, local moment, cashier, payment,
/// total in whole COP and the state distinguished by text AND color
/// (Vigente = green text, Anulada = red; legible without color).
/// </summary>
public sealed record SaleHistoryRowViewModel(
    long SaleNumber,
    string NumberText,
    string MomentText,
    string CashierName,
    string PaymentText,
    string TotalText,
    string StatusText,
    Brush StatusBrush)
{
    // Congelados: un Freezable compartido es válido en cualquier hilo STA
    // (las pruebas de UI crean un STA por caso; sin Freeze, el binding
    // "DependencySource en el mismo subproceso" revienta al cruzar hilos).
    private static readonly Brush ActiveBrush = CreateFrozenBrush(0x15, 0x80, 0x3D);
    private static readonly Brush VoidedBrush = CreateFrozenBrush(0xB9, 0x1C, 0x1C);

    private static Brush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static SaleHistoryRowViewModel From(SaleHistoryItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var local = DateTimeOffset.FromUnixTimeMilliseconds(dto.SoldAtUtcMs)
            .ToOffset(TimeSpan.FromMinutes(dto.SoldAtOffsetMinutes));
        var voided = dto.Status == "VOIDED";
        return new SaleHistoryRowViewModel(
            dto.SaleNumber,
            $"V-{dto.SaleNumber:D6}",
            local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            dto.CashierName,
            dto.PaymentMethod == "CASH" ? "Efectivo" : "Transferencia",
            MoneyText.FormatCop(dto.TotalCop),
            voided ? "Anulada" : "Vigente",
            voided ? VoidedBrush : ActiveBrush);
    }
}
