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
/// Administrative history page (JHO-46, P-UX §13.1/13.2): Desde/Hasta
/// required (Hasta never before Desde), Enter searches, 50 per page,
/// newest first, Anterior/Siguiente reflect availability and the detail
/// opens per row. The list is read-only; voiding happens in the detail.
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
    private int page = 1;
    private int totalCount;
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

        var today = DateOnly.FromDateTime(DateTime.Today);
        fromText = today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        toText = fromText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SaleHistoryRowViewModel> Items => items;

    public string FromText
    {
        get => fromText;
        set => SetProperty(ref fromText, value);
    }

    public string ToText
    {
        get => toText;
        set => SetProperty(ref toText, value);
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

    /// <summary>JHO-60: la página carga el rango por defecto (hoy) sin pulsar Buscar.</summary>
    public Task InitializeAsync() => SearchAsync(1);
    public async Task SearchAsync(int targetPage = 1)
    {
        if (!TryParseDate(FromText, out var from) || !TryParseDate(ToText, out var to))
        {
            ErrorMessage = "Ingrese las fechas Desde y Hasta con formato dd/MM/aaaa.";
            return;
        }

        var result = await searchHistory.ExecuteAsync(
            new SearchSalesHistoryRequest(from, to, targetPage), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        page = result.Value.Page;
        totalCount = result.Value.TotalCount;
        items.Clear();
        foreach (var item in result.Value.Items)
        {
            items.Add(SaleHistoryRowViewModel.From(item));
        }

        SelectedItem = items.FirstOrDefault();
        SummaryText = totalCount == 0
            ? "Sin ventas en el rango."
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
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));
    private static readonly Brush VoidedBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));

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
