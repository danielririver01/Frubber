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
    FinalizeWorkstationSaleUseCase checkout) : INotifyPropertyChanged
{
    private readonly DraftSale draft = new();
    private SignedInUser? signedIn;
    private long? cashId;
    private bool busy;
    private bool reviewPending;
    private bool closePending;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string TerminalName => terminal.Name;
    public IReadOnlyList<ActiveUserDto> Users { get; } = users;
    public ActiveUserDto? SelectedUser { get; set; } = users.FirstOrDefault();
    public bool ShowLogin => signedIn is null;
    public bool ShowAdminMenu => signedIn?.Role == FrubberPOS.Domain.Users.UserRole.Admin;
    public bool ShowCash => signedIn is not null && !cashId.HasValue;
    public bool ShowSale => signedIn is not null && cashId.HasValue;
    public bool CanInteract => !busy;
    public bool CanEditSale => !reviewPending && !closePending;
    public bool CanCloseCash => cashId.HasValue && !reviewPending && draft.IsEmpty;
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
        if (!CanEditSale)
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

        var result = await checkout.ExecuteAsync(new(draft.Lines.ToArray(), draft.DiscountCop,
            IsCash ? "CASH" : "TRANSFER", received), token);
        if (ShowError(result))
        {
            reviewPending = result.Error?.Code == ErrorCode.Storage;
            return;
        }

        draft.Clear();
        Received = string.Empty;
        Message = $"Venta V-{result.Value.SaleNumber:D6} registrada · {MoneyText.FormatCop(result.Value.TotalCop)}";
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
