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
    private readonly INavigationService navigation;

    private OpenCashSessionDto? session;
    private string openingBalanceText = "0";
    private string errorMessage = string.Empty;

    public CashViewModel(
        GetOpenCashSessionUseCase getOpen,
        OpenCashSessionUseCase openSession,
        CloseCashSessionUseCase closeSession,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(getOpen);
        ArgumentNullException.ThrowIfNull(openSession);
        ArgumentNullException.ThrowIfNull(closeSession);
        ArgumentNullException.ThrowIfNull(navigation);
        this.getOpen = getOpen;
        this.openSession = openSession;
        this.closeSession = closeSession;
        this.navigation = navigation;

        OpenCommand = new AsyncCommand(_ => OpenAsync());
        CloseCommand = new AsyncCommand(_ => CloseAsync(), () => IsOpen);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsOpen => session is not null;

    public string StateText => IsOpen ? "Caja abierta" : "Sin caja";

    public string OpenedByText => session is null ? "—" : session.OpenedByNameSnapshot;

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

    public AsyncCommand OpenCommand { get; }

    public AsyncCommand CloseCommand { get; }

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
