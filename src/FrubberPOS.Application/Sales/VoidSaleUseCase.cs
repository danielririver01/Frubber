using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.System;
using FrubberPOS.Application.Trial;

namespace FrubberPOS.Application.Sales;

/// <summary>Void context read in one pass (JHO-49): the sale and its cash session gate.</summary>
public sealed record SaleVoidContext(int SaleId, int CashSessionId, bool IsVoided, bool CashSessionOpen);

/// <summary>Line needed to reintegrate stock on a full void (RN-HIS-008).</summary>
public sealed record SaleVoidLine(int ProductId, long QuantityMilli);

/// <summary>Sale void port (JHO-49). All writes run inside the caller's transaction.</summary>
public interface ISaleVoidRepository
{
    /// <summary>Sale + void + cash-session gate; <c>null</c> when the number is unknown.</summary>
    Task<SaleVoidContext?> FindVoidContextAsync(IUnitOfWork unit, long saleNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<SaleVoidLine>> GetLinesForVoidAsync(IUnitOfWork unit, int saleId, CancellationToken cancellationToken);

    /// <summary>Marks the sale as voided with the administrator and the moment (RN-HIS-010).</summary>
    Task InsertVoidAsync(
        IUnitOfWork unit,
        int saleId,
        long voidedAtUtcMs,
        int voidedAtOffsetMinutes,
        int adminUserId,
        string adminNameSnapshot,
        CancellationToken cancellationToken);
}

public sealed record VoidSaleRequest(long SaleNumber);

public sealed record VoidSaleResponse(long SaleNumber);

/// <summary>
/// Full transactional void (JHO-49, RN-HIS-005…010): administrator only,
/// the sale must be active and its cash session still open; one atomic
/// write marks the sale, records administrator + moment (no written
/// motive) and reintegrates inventory. Repeating the void is refused
/// before any write, so the reintegration can never duplicate.
/// </summary>
public sealed class VoidSaleUseCase : IUseCase<VoidSaleRequest, VoidSaleResponse>
{
    private readonly ISaleVoidRepository voids;
    private readonly ISaleRepository sales;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;
    private readonly IClock clock;
    private readonly IOperationalGate gate;

    public VoidSaleUseCase(
        ISaleVoidRepository voids,
        ISaleRepository sales,
        IUnitOfWorkFactory units,
        ICurrentSession session,
        IClock clock,
        IOperationalGate gate)
    {
        ArgumentNullException.ThrowIfNull(voids);
        ArgumentNullException.ThrowIfNull(sales);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(gate);
        this.voids = voids;
        this.sales = sales;
        this.units = units;
        this.session = session;
        this.clock = clock;
        this.gate = gate;
    }

    public async Task<Result<VoidSaleResponse>> ExecuteAsync(VoidSaleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<VoidSaleResponse>.Failure(denied);
        }

        if (request.SaleNumber <= 0)
        {
            return Result<VoidSaleResponse>.Failure(new Error(ErrorCode.Validation, "El número de venta no es válido."));
        }

        // P-DD §11.3/9.5: the operational state (license/clock) is validated again here.
        var blocked = await gate.CheckAsync(cancellationToken);
        if (blocked is not null)
        {
            return Result<VoidSaleResponse>.Failure(blocked);
        }

        var current = session.Current!;
        var saleNumberText = $"V-{request.SaleNumber:D6}";

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        var context = await voids.FindVoidContextAsync(unit, request.SaleNumber, cancellationToken);
        if (context is null)
        {
            return Result<VoidSaleResponse>.Failure(new Error(ErrorCode.NotFound, $"No se encontró la venta {saleNumberText}."));
        }

        if (context.IsVoided)
        {
            return Result<VoidSaleResponse>.Failure(new Error(ErrorCode.Conflict, $"La venta {saleNumberText} ya está anulada."));
        }

        if (!context.CashSessionOpen)
        {
            return Result<VoidSaleResponse>.Failure(new Error(
                ErrorCode.Conflict, $"La venta {saleNumberText} solo puede anularse mientras su caja siga abierta."));
        }

        var now = clock.UtcNow;
        await voids.InsertVoidAsync(
            unit, context.SaleId, now.ToUnixTimeMilliseconds(), (int)now.Offset.TotalMinutes,
            current.UserId, current.DisplayName, cancellationToken);

        foreach (var line in await voids.GetLinesForVoidAsync(unit, context.SaleId, cancellationToken))
        {
            await sales.AdjustStockAsync(unit, line.ProductId, line.QuantityMilli, cancellationToken);
        }

        await unit.CommitAsync(cancellationToken);
        return Result<VoidSaleResponse>.Success(new VoidSaleResponse(request.SaleNumber));
    }
}
