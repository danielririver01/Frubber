using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

/// <summary>
/// Cash screen (JHO-36, mockup fig. 5): opening form when closed
/// (initial balance editable, starts at 0; never negative) and read-only
/// summary when open — opening, user, initial balance, cash/transfer/
/// discounts/net/expected and sale count (§11.2). Closing asks the
/// irreversible confirmation (§11.3). F10 selects this page (shell-level,
/// wired in MainWindow).
/// </summary>
public sealed class CashViewModel : INotifyPropertyChanged
{
    private readonly GetOpenCashSessionUseCase getOpen;
    private readonly OpenCashSessionUseCase openSession;
    private readonly CloseCashSessionUseCase closeSession;
    private readonly CorrectOpeningBalanceUseCase correctBalance;
    private readonly Application.Authentication.ICurrentSession currentSession;
    private readonly INavigationService navigation;

    private OpenCashSessionDto? session;
    private string openingBalanceText = "0";
    private string errorMessage = string.Empty;
    private string statusMessage = string.Empty;

    public CashViewModel(
        GetOpenCashSessionUseCase getOpen,
        OpenCashSessionUseCase openSession,
        CloseCashSessionUseCase closeSession,
        CorrectOpeningBalanceUseCase correctBalance,
        Application.Authentication.ICurrentSession currentSession,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(getOpen);
        ArgumentNullException.ThrowIfNull(openSession);
        ArgumentNullException.ThrowIfNull(closeSession);
        ArgumentNullException.ThrowIfNull(correctBalance);
        ArgumentNullException.ThrowIfNull(currentSession);
        ArgumentNullException.ThrowIfNull(navigation);
        this.getOpen = getOpen;
        this.openSession = openSession;
        this.closeSession = closeSession;
        this.correctBalance = correctBalance;
        this.currentSession = currentSession;
        this.navigation = navigation;

        OpenCommand = new AsyncCommand(_ => OpenAsync());
        CloseCommand = new AsyncCommand(_ => CloseAsync(), () => IsOpen);
        CorrectBalanceCommand = new AsyncCommand(_ => CorrectBalanceAsync(), () => CanCorrectBalance);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsOpen => session is not null;

    public string StateText => IsOpen ? "Caja abierta" : "Sin caja";

    public string OpenedByText => session is null ? "—" : session.OpenedByNameSnapshot;

    /// <summary>"28 sep 2026 · 8:02 a. m." — apertura en hora local (§11.2).</summary>
    public string OpenedAtText => session is null
        ? "—"
        : DateTimeOffset.FromUnixTimeMilliseconds(session.OpenedAtUtcMs)
            .ToOffset(TimeSpan.FromMinutes(session.OpenedAtOffsetMinutes))
            .ToString("dd/MM/yyyy · HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public string SessionOpeningBalanceDisplay => session is null ? "—" : MoneyText.FormatCop(session.OpeningBalanceCop);

    public string CashSalesText => session is null ? "—" : MoneyText.FormatCop(session.CashSalesCop);

    public string TransferSalesText => session is null ? "—" : MoneyText.FormatCop(session.TransferSalesCop);

    public string DiscountText => session is null ? "—" : MoneyText.FormatCop(session.DiscountCop);

    public string NetSalesText => session is null ? "—" : MoneyText.FormatCop(session.NetSalesCop);

    public string SaleCountText => session is null ? "—" : session.SaleCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ventas";

    public string ExpectedCashText => session is null ? "—" : MoneyText.FormatCop(session.ExpectedCashCop);

    public string OpeningBalanceText
    {
        get => openingBalanceText;
        set => SetProperty(ref openingBalanceText, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    /// <summary>JHO-61: corregir saldo inicial — solo ADMIN con caja abierta.</summary>
    public bool ShowCorrectBalance =>
        IsOpen && currentSession.Current?.Role == Domain.Users.UserRole.Admin;

    /// <summary>
    /// JHO-82: el clic siempre responde — sin ventas abre el diálogo; con
    /// ventas el guard de <see cref="CorrectBalanceAsync"/> muestra el
    /// mensaje de cerrar y reabrir (antes el botón quedaba deshabilitado).
    /// </summary>
    public bool CanCorrectBalance => ShowCorrectBalance;

    public AsyncCommand OpenCommand { get; }

    public AsyncCommand CloseCommand { get; }

    public AsyncCommand CorrectBalanceCommand { get; }

    public async Task RefreshAsync()
    {
        var result = await getOpen.ExecuteAsync(new GetOpenCashSessionRequest(), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        ErrorMessage = string.Empty;
        session = result.Value.Session;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        CloseCommand.RaiseCanExecuteChanged();
        CorrectBalanceCommand.RaiseCanExecuteChanged();
    }

    /// <summary>JHO-61: corregir saldo mal digitado — diálogo con el valor actual.</summary>
    public async Task CorrectBalanceAsync()
    {
        ErrorMessage = string.Empty;
        if (session is null || session.SaleCount > 0)
        {
            ErrorMessage = "La caja ya tiene ventas registradas. Ciérrela y abra una nueva con el saldo correcto.";
            return;
        }

        var corrected = navigation.AskOpeningBalanceCorrection(session.OpeningBalanceCop);
        if (corrected is null)
        {
            return;
        }

        var result = await correctBalance.ExecuteAsync(
            new CorrectOpeningBalanceRequest(corrected.Value), CancellationToken.None);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.Message;
            return;
        }

        StatusMessage = $"Saldo inicial corregido a {MoneyText.FormatCop(corrected.Value)}.";
        await RefreshAsync();
    }

    public async Task OpenAsync()
    {
        ErrorMessage = string.Empty;
        if (!MoneyText.TryParseCop(OpeningBalanceText, out var balance))
        {
            ErrorMessage = "Escriba el saldo inicial en pesos enteros.";
            return;
        }

        var opened = await openSession.ExecuteAsync(new OpenCashSessionRequest(balance), CancellationToken.None);
        if (!opened.IsSuccess)
        {
            ErrorMessage = opened.Error!.Message;
            return;
        }

        await RefreshAsync();
    }

    public async Task CloseAsync()
    {
        ErrorMessage = string.Empty;
        if (session is null || !navigation.ConfirmCashClosure())
        {
            return;
        }

        var closed = await closeSession.ExecuteAsync(new CloseCashSessionRequest(), CancellationToken.None);
        if (!closed.IsSuccess)
        {
            ErrorMessage = closed.Error!.Message;
            return;
        }

        await RefreshAsync();
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
