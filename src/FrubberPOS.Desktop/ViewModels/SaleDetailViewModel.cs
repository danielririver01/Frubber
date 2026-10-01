using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.Common;
using QtyFormat = FrubberPOS.Desktop.Common.QuantityText;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Historical sale detail (JHO-46, P-UX §13.3/13.4): read-only financial
/// header, session, cashier, snapshot lines and payment. "Anular venta"
/// only shows for ADMIN and enables while the sale is active and its cash
/// session stays open (RN-HIS-005/007); F9 confirms, Esc cancels, and the
/// confirmation states the inventory reintegration without asking motive.
/// </summary>
public sealed class SaleDetailViewModel : INotifyPropertyChanged
{
    private readonly GetSaleDetailUseCase getDetail;
    private readonly VoidSaleUseCase voidSale;
    private readonly INavigationService navigation;
    private readonly ObservableCollection<SaleDetailLineViewModel> lines = [];

    private long saleNumber;
    private bool isAdmin;
    private SaleDetailDto? detail;
    private string errorMessage = string.Empty;
    private bool voidedHere;

    public SaleDetailViewModel(
        GetSaleDetailUseCase getDetail,
        VoidSaleUseCase voidSale,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(getDetail);
        ArgumentNullException.ThrowIfNull(voidSale);
        ArgumentNullException.ThrowIfNull(navigation);
        this.getDetail = getDetail;
        this.voidSale = voidSale;
        this.navigation = navigation;

        VoidCommand = new AsyncCommand(_ => VoidAsync(), () => CanVoid);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised to close the dialog; true = la venta quedó anulada aquí.</summary>
    public event EventHandler<bool>? RequestClose;

    public ObservableCollection<SaleDetailLineViewModel> Lines => lines;

    public bool IsVoided => detail?.Status == "VOIDED";

    /// <summary>True when the void executed inside this dialog (parent refreshes).</summary>
    public bool VoidedHere => voidedHere;

    public string SaleNumberText { get; private set; } = string.Empty;

    public string MomentText { get; private set; } = string.Empty;

    public string CashSessionText { get; private set; } = string.Empty;

    public string CashierText { get; private set; } = string.Empty;

    public string PaymentText { get; private set; } = string.Empty;

    public string CashDetailText { get; private set; } = string.Empty;

    public string SubtotalText { get; private set; } = string.Empty;

    public string DiscountText { get; private set; } = string.Empty;

    public string TotalText { get; private set; } = string.Empty;

    public string StatusText { get; private set; } = string.Empty;

    public Brush StatusBrush { get; private set; } = Brushes.Black;

    public string VoidInfoText { get; private set; } = string.Empty;

    public bool VoidInfoVisible => IsVoided;

    /// <summary>P-UX §13.4: solo ADMIN, vigente y con su caja aún abierta.</summary>
    public bool CanVoid => isAdmin && detail is { Status: "ACTIVE", CashSessionOpen: true };

    public bool VoidButtonVisible => isAdmin;

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public AsyncCommand VoidCommand { get; }

    public async Task LoadAsync(long saleNumberToLoad, bool viewerIsAdmin)
    {
        saleNumber = saleNumberToLoad;
        isAdmin = viewerIsAdmin;

        var result = await getDetail.ExecuteAsync(new GetSaleDetailRequest(saleNumber), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        Apply(result.Value);
    }

    public async Task VoidAsync()
    {
        if (!CanVoid)
        {
            return;
        }

        if (!navigation.ConfirmSaleVoid(saleNumber))
        {
            return;
        }

        var result = await voidSale.ExecuteAsync(new VoidSaleRequest(saleNumber), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        voidedHere = true;

        // Reload from history: the void info (admin + moment) surfaces.
        var reloaded = await getDetail.ExecuteAsync(new GetSaleDetailRequest(saleNumber), CancellationToken.None);
        if (reloaded.IsSuccess)
        {
            Apply(reloaded.Value);
        }
    }

    public void Close() => RequestClose?.Invoke(this, voidedHere);

    private void Apply(SaleDetailDto value)
    {
        detail = value;
        ErrorMessage = string.Empty;

        SaleNumberText = $"V-{value.SaleNumber:D6}";
        MomentText = FormatMoment(value.SoldAtUtcMs, value.SoldAtOffsetMinutes);
        CashSessionText = $"Caja #{value.CashSessionId}";
        CashierText = $"Cajero: {value.CashierName}";
        PaymentText = value.PaymentMethod == "CASH" ? "Medio de pago: Efectivo" : "Medio de pago: Transferencia";
        CashDetailText = value.PaymentMethod == "CASH" && value.ReceivedCop is not null
            ? $"Recibido: {MoneyText.FormatCop(value.ReceivedCop.Value)}   Cambio: {MoneyText.FormatCop(value.ChangeCop ?? 0)}"
            : string.Empty;
        SubtotalText = MoneyText.FormatCop(value.SubtotalCop);
        DiscountText = MoneyText.FormatCop(value.DiscountCop);
        TotalText = MoneyText.FormatCop(value.TotalCop);
        StatusText = value.Status == "VOIDED" ? "Anulada" : "Vigente";
        StatusBrush = value.Status == "VOIDED"
            ? new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))
            : new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));
        VoidInfoText = value.Status == "VOIDED"
            ? $"Anulada por {value.VoidedByAdminName} el {FormatMoment(value.VoidedAtUtcMs ?? 0, value.VoidedAtOffsetMinutes ?? 0)}"
            : string.Empty;

        lines.Clear();
        foreach (var line in value.Lines)
        {
            lines.Add(SaleDetailLineViewModel.From(line));
        }

        OnPropertyChanged(nameof(SaleNumberText));
        OnPropertyChanged(nameof(MomentText));
        OnPropertyChanged(nameof(CashSessionText));
        OnPropertyChanged(nameof(CashierText));
        OnPropertyChanged(nameof(PaymentText));
        OnPropertyChanged(nameof(CashDetailText));
        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(DiscountText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(VoidInfoText));
        OnPropertyChanged(nameof(VoidInfoVisible));
        OnPropertyChanged(nameof(CanVoid));
        OnPropertyChanged(nameof(VoidButtonVisible));
        VoidCommand.RaiseCanExecuteChanged();
    }

    private static string FormatMoment(long utcMs, int offsetMinutes) =>
        DateTimeOffset.FromUnixTimeMilliseconds(utcMs)
            .ToOffset(TimeSpan.FromMinutes(offsetMinutes))
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

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

/// <summary>Snapshot line (RN-HIS-002): number, PLU, name, quantity + unit, price, amount.</summary>
public sealed record SaleDetailLineViewModel(
    int LineNumber,
    string Plu,
    string Name,
    string QuantityText,
    string PriceText,
    string TotalText)
{
    public static SaleDetailLineViewModel From(SaleDetailLineDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var unit = Domain.Products.ProductUnits.Parse(dto.UnitCode);
        return new SaleDetailLineViewModel(
            dto.LineNumber,
            dto.Plu,
            dto.Name,
            QtyFormat.Format(dto.QuantityMilli, unit) + " " + dto.UnitCode,
            MoneyText.FormatCop(dto.UnitPriceCop),
            MoneyText.FormatCop(dto.LineTotalCop));
    }
}
