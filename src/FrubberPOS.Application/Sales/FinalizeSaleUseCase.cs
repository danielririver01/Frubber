using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.System;

namespace FrubberPOS.Application.Sales;

/// <summary>Persisted sale line snapshot (JHO-43).</summary>
public sealed record SaleLinePersist(
    int ProductId,
    string PluSnapshot,
    string NameSnapshot,
    string UnitSnapshot,
    long UnitPriceCop,
    long QuantityMilli);

/// <summary>Sale persistence port (P-DD §8.2).</summary>
public interface ISaleRepository
{
    /// <summary>Consumes and returns the next sale number inside the caller's transaction.</summary>
    Task<long> NextSaleNumberAsync(IUnitOfWork unit, CancellationToken cancellationToken);

    Task<int> InsertSaleAsync(
        IUnitOfWork unit,
        long saleNumber,
        int cashSessionId,
        int cashierUserId,
        string cashierNameSnapshot,
        long soldAtUtcMs,
        int soldAtOffsetMinutes,
        long subtotalCop,
        long discountCop,
        string paymentMethod,
        long? receivedCop,
        CancellationToken cancellationToken);

    Task InsertLineAsync(IUnitOfWork unit, int saleId, int lineNumber, SaleLinePersist line, CancellationToken cancellationToken);

    /// <summary>Adjusts stock by delta (negative = subtract) and locks the unit (RN-INV-001).</summary>
    Task AdjustStockAsync(IUnitOfWork unit, int productId, long deltaMilli, CancellationToken cancellationToken);

    /// <summary>Current row for revalidation: <c>null</c> when the product disappeared.</summary>
    Task<ProductPriceRow?> FindProductRowAsync(IUnitOfWork unit, int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Historical sale header + lines for the ticket (JHO-45). Read-only;
    /// <c>null</c> when no sale owns that number.
    /// </summary>
    Task<TicketSaleData?> FindSaleForTicketAsync(IUnitOfWork unit, long saleNumber, CancellationToken cancellationToken);
}

public sealed class ProductPriceRow
{
    public int ProductId { get; set; }

    public string Plu { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public long PriceCop { get; set; }

    public long StockMilli { get; set; }

    public bool IsActive { get; set; }
}

public sealed record FinalizeSaleRequest(
    IReadOnlyList<DraftSaleLine> Lines,
    long DiscountCop,
    string PaymentMethod,
    long? ReceivedCop);

public sealed record FinalizeSaleResponse(long SaleNumber, int SaleId, long TotalCop);

/// <summary>
/// Transactional sale finalization (JHO-43, RN-CAJ-002, RN-VEN-001/002):
/// one unit of work, one write lock — open session check, product
/// revalidation, header+lines+inventory+numbering all-or-nothing. Any
/// failure rolls everything back; SaleNumber gaps never leak.
/// </summary>
public sealed class FinalizeSaleUseCase : IUseCase<FinalizeSaleRequest, FinalizeSaleResponse>
{
    private readonly ISaleRepository sales;
    private readonly ICashSessionRepository sessions;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;
    private readonly IClock clock;

    public FinalizeSaleUseCase(
        ISaleRepository sales,
        ICashSessionRepository sessions,
        IUnitOfWorkFactory units,
        ICurrentSession session,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(sales);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        this.sales = sales;
        this.sessions = sessions;
        this.units = units;
        this.session = session;
        this.clock = clock;
    }

    public async Task<Result<FinalizeSaleResponse>> ExecuteAsync(FinalizeSaleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Result<FinalizeSaleResponse>.Failure(new Error(ErrorCode.Validation, "La venta no puede estar vacía."));
        }

        if (request.PaymentMethod is not ("CASH" or "TRANSFER"))
        {
            return Result<FinalizeSaleResponse>.Failure(new Error(ErrorCode.Validation, "El medio de pago no es válido."));
        }

        if (request.PaymentMethod == "CASH" && (request.ReceivedCop is null || request.ReceivedCop < 0))
        {
            return Result<FinalizeSaleResponse>.Failure(new Error(ErrorCode.Validation, "Efectivo recibido es obligatorio para cobrar en efectivo."));
        }

        if (request.PaymentMethod == "TRANSFER" && request.ReceivedCop is not null)
        {
            return Result<FinalizeSaleResponse>.Failure(new Error(ErrorCode.Validation, "Transferencia no admite recibido/cambio."));
        }

        var current = session.Current;
        if (current is null)
        {
            return Result<FinalizeSaleResponse>.Failure(new Error(ErrorCode.Authorization, "Debe iniciar sesión."));
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        var openSession = await sessions.FindOpenAsync(unit, cancellationToken);
        if (openSession is null)
        {
            return Result<FinalizeSaleResponse>.Failure(new Error(
                ErrorCode.Conflict, "No hay caja abierta. Abra caja antes de cobrar."));
        }

        foreach (var line in request.Lines)
        {
            if (line.QuantityMilli <= 0)
            {
                return Result<FinalizeSaleResponse>.Failure(new Error(
                    ErrorCode.Validation, $"La línea «{line.Name}» no tiene cantidad válida."));
            }

            if (Domain.Products.ProductUnits.ToCode(line.Unit) == "UNIT" && line.QuantityMilli % Domain.Products.ProductQuantities.Scale != 0)
            {
                return Result<FinalizeSaleResponse>.Failure(new Error(
                    ErrorCode.Validation, $"La cantidad de «{line.Name}» debe ser entera."));
            }
        }

        long subtotal = 0;
        var revalidated = new List<SaleLinePersist>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            var row = await sales.FindProductRowAsync(unit, line.ProductId, cancellationToken);
            if (row is null)
            {
                return Result<FinalizeSaleResponse>.Failure(new Error(
                    ErrorCode.NotFound, $"«{line.Name}» ya no existe en el catálogo."));
            }

            if (!row.IsActive)
            {
                return Result<FinalizeSaleResponse>.Failure(new Error(
                    ErrorCode.Conflict, $"«{line.Name}» está inactivo."));
            }

            if (row.PriceCop != line.UnitPriceCop)
            {
                return Result<FinalizeSaleResponse>.Failure(new Error(
                    ErrorCode.Conflict, $"El precio de «{line.Name}» cambió. Revise la venta."));
            }

            if (row.Unit != Domain.Products.ProductUnits.ToCode(line.Unit))
            {
                return Result<FinalizeSaleResponse>.Failure(new Error(
                    ErrorCode.Conflict, $"La unidad de «{line.Name}» cambió. Revise la venta."));
            }

            if (row.Plu != line.Plu || row.Name != line.Name)
            {
                return Result<FinalizeSaleResponse>.Failure(new Error(
                    ErrorCode.Conflict, $"«{line.Name}» cambió en el catálogo. Revise la venta."));
            }

            subtotal += line.LineTotalCop;
            revalidated.Add(new SaleLinePersist(
                line.ProductId, line.Plu, line.Name,
                row.Unit, line.UnitPriceCop, line.QuantityMilli));
        }

        var total = subtotal - request.DiscountCop;
        if (total <= 0 || (request.PaymentMethod == "CASH" && request.ReceivedCop! < total))
        {
            return Result<FinalizeSaleResponse>.Failure(new Error(
                ErrorCode.Validation, "El total no es válido o el efectivo recibido no alcanza."));
        }

        var now = clock.UtcNow;
        var saleNumber = await sales.NextSaleNumberAsync(unit, cancellationToken);

        var saleId = await sales.InsertSaleAsync(
            unit,
            saleNumber,
            openSession.CashSessionId,
            current.UserId,
            current.DisplayName,
            now.ToUnixTimeMilliseconds(),
            (int)now.Offset.TotalMinutes,
            subtotal,
            request.DiscountCop,
            request.PaymentMethod,
            request.ReceivedCop,
            cancellationToken);

        var lineNumber = 1;
        foreach (var line in revalidated)
        {
            await sales.InsertLineAsync(unit, saleId, lineNumber++, line, cancellationToken);
            await sales.AdjustStockAsync(unit, line.ProductId, -line.QuantityMilli, cancellationToken);
        }

        await unit.CommitAsync(cancellationToken);
        return Result<FinalizeSaleResponse>.Success(new FinalizeSaleResponse(saleNumber, saleId, total));
    }
}
