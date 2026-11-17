using System.ComponentModel;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Users;
using FrubberPOS.Application.Workstations;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

internal sealed class WorkstationViewModel(
    WorkstationInfo terminal,
    IReadOnlyList<ActiveUserDto> users,
    IWorkstationStore store,
    AuthenticateWorkstationUserUseCase authenticate,
    OpenWorkstationCashUseCase openCash,
    CloseWorkstationCashUseCase closeCash,
    FinalizeWorkstationSaleUseCase checkout,
    IWorkstationSaleRecovery? recovery = null) : INotifyPropertyChanged
{
    private readonly DraftSale draft = new();
    private SignedInUser? signedIn;
    private long? cashId;
    private bool busy;
    private bool reviewPending;
    private bool closePending;
    private bool receiptDisplayed;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string TerminalName => terminal.Name;
    public IReadOnlyList<ActiveUserDto> Users { get; } = users;
    public ActiveUserDto? SelectedUser { get; set; } = users.FirstOrDefault();
    public bool ShowLogin => signedIn is null;
    public bool ShowAdminMenu => signedIn?.Role == FrubberPOS.Domain.Users.UserRole.Admin;
    public bool ShowRecovery => signedIn is not null && recovery?.HasPendingAttempt == true;
    public bool CanAcknowledgeReceipt => ShowRecovery && receiptDisplayed;
    public bool ShowCash => signedIn is not null && !cashId.HasValue && !ShowRecovery;
    public bool ShowSale => signedIn is not null && cashId.HasValue && !ShowRecovery;
    public bool CanInteract => !busy;
    public bool CanEditSale => !reviewPending && !closePending && !ShowRecovery;
    public bool CanCloseCash => cashId.HasValue && !reviewPending && !ShowRecovery && draft.IsEmpty;
    public string UserName => signedIn?.DisplayName ?? string.Empty;
    public string OpeningBalance { get; set; } = "0";
    public string Plu { get; set; } = string.Empty;
    public string Quantity { get; set; } = "1";
    public string Received { get; set; } = string.Empty;
    public bool IsCash { get; set; } = true;
    public string Total => MoneyText.FormatCop(draft.TotalCop);
    public string Message { get; private set; } = string.Empty;
    public IReadOnlyList<string> Lines => draft.Lines.Select(line =>
        $"{line.Name} · {QuantityText.Format(line.QuantityMilli, line.Unit)} · {MoneyText.FormatCop(line.LineTotalCop)}").ToArray();

    public Task SignInAsync(string pin) => RunAsync(async token =>
    {
        if (SelectedUser is not { } user)
        {
            Message = "Seleccione un usuario.";
            return;
        }

        var login = await authenticate.ExecuteAsync(new(user.UserId, pin), token);
        if (!ShowError(login))
        {
            var cash = await store.GetCashAsync(token);
            if (!ShowError(cash))
            {
                signedIn = login.Value;
                cashId = cash.Value.CashId;
            }
        }
    });

    public Task OpenCashAsync() => RunAsync(async token =>
    {
        if (ShowRecovery)
        {
            return;
        }
        if (!MoneyText.TryParseCop(OpeningBalance, out var balance))
        {
            Message = "Ingrese un saldo inicial válido.";
            return;
        }

        var result = await openCash.ExecuteAsync(new(balance), token);
        if (!ShowError(result))
        {
            cashId = result.Value;
            Message = "Caja abierta.";
        }
        else if (result.Error?.Code == ErrorCode.Conflict)
        {
            var cash = await store.GetCashAsync(token);
            cashId = cash.IsSuccess ? cash.Value.CashId : null;
        }
    });

    public Task CloseCashAsync() => RunAsync(async token =>
    {
        if (!CanCloseCash || cashId is not { } id)
        {
            return;
        }

        var result = await closeCash.ExecuteAsync(new(id), token);
        if (ShowError(result))
        {
            closePending |= result.Error?.Code == ErrorCode.Storage;
            return;
        }

        var closure = result.Value;
        cashId = null;
        closePending = false;
        OpeningBalance = "0";
        Message = $"Caja cerrada · {closure.SaleCount} ventas · Total {MoneyText.FormatCop(closure.NetSales)}\n" +
            $"Saldo inicial {MoneyText.FormatCop(closure.OpeningBalance)} · Ventas en efectivo {MoneyText.FormatCop(closure.CashSales)}\n" +
            $"Transferencias {MoneyText.FormatCop(closure.TransferSales)} · Efectivo esperado {MoneyText.FormatCop(closure.ExpectedCash)}";
    });

    public Task AddProductAsync() => RunAsync(async token =>
    {
        if (!CanEditSale)
        {
            return;
        }
        var plu = Plu.Trim().ToUpperInvariant();
        if (plu.Length is 0 or > 32)
        {
            Message = "Ingrese el PLU del producto.";
            return;
        }

        var product = await store.FindProductAsync(plu, token);
        if (ShowError(product))
        {
            return;
        }

        if (!QuantityText.TryParseToMilli(Quantity, product.Value.Unit, out var quantity))
        {
            Message = "Ingrese una cantidad válida.";
            return;
        }

        if (!ShowError(draft.AddOrAccumulate(product.Value, quantity)))
        {
            Plu = string.Empty;
            Quantity = "1";
        }
    });

    public Task CheckoutAsync() => RunAsync(async token =>
    {
        if (!CanEditSale || (recovery is not null && cashId is null))
        {
            return;
        }

        long? received = null;
        if (IsCash)
        {
            if (!MoneyText.TryParseCop(Received, out var cash))
            {
                Message = "Ingrese el efectivo recibido.";
                return;
            }
            received = cash;
        }

        var request = new FinalizeSaleRequest(draft.Lines.ToArray(), draft.DiscountCop,
            IsCash ? "CASH" : "TRANSFER", received);
        var result = recovery is not null && cashId is { } currentCash
            ? await recovery.RegisterAsync(currentCash, request, token)
            : await checkout.ExecuteAsync(request, token);
        if (ShowError(result))
        {
            reviewPending = recovery is null && result.Error?.Code == ErrorCode.Storage;
            return;
        }

        draft.Clear();
        Received = string.Empty;
        receiptDisplayed = recovery is not null;
        Message = $"Venta V-{result.Value.SaleNumber:D6} registrada · {MoneyText.FormatCop(result.Value.TotalCop)}";
    });

    public Task RecoverSaleAsync() => RunAsync(async token =>
    {
        if (recovery is null || !ShowRecovery)
        {
            return;
        }

        var result = await recovery.RecoverAsync(token);
        if (!ShowError(result))
        {
            draft.Clear();
            Received = string.Empty;
            receiptDisplayed = true;
            Message = $"Venta V-{result.Value.SaleNumber:D6} recuperada · {MoneyText.FormatCop(result.Value.TotalCop)}";
        }
    });

    public Task AcknowledgeReceiptAsync() => RunAsync(async token =>
    {
        if (recovery is null || !CanAcknowledgeReceipt)
        {
            return;
        }

        try
        {
            recovery.Acknowledge();
            receiptDisplayed = false;
            var cash = await store.GetCashAsync(token);
            cashId = ShowError(cash) ? null : cash.Value.CashId;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            Message = "No se pudo cerrar la revisión del comprobante.";
        }
    });

    public void ClearSale()
    {
        if (!busy && CanEditSale)
        {
            draft.Clear();
            Received = string.Empty;
            Message = string.Empty;
            Refresh();
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        Message = string.Empty;
        Refresh();
        try
        {
            await action(CancellationToken.None);
        }
        finally
        {
            busy = false;
            Refresh();
        }
    }

    private bool ShowError(Result result)
    {
        if (result.Error is not { } error)
        {
            return false;
        }

        Message = error.Message;
        return true;
    }

    private void Refresh() => PropertyChanged?.Invoke(this, new(string.Empty));
}
