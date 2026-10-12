using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.System;
using FrubberPOS.Application.Trial;

namespace FrubberPOS.Application.Cash;

/// <summary>
/// Financial summary of any cash session, open or closed (JHO-35): the
/// numbers come from valid sales while open and stay frozen after closing
/// — later voids never reopen them (RN-CAJ-010). Any signed-in role reads;
/// the cashier screen (JHO-36) uses it to render the panel.
/// </summary>
public sealed class GetCashSessionSummaryUseCase : IUseCase<GetCashSessionSummaryRequest, GetCashSessionSummaryResponse>
{
    private readonly ICashSessionRepository sessions;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public GetCashSessionSummaryUseCase(ICashSessionRepository sessions, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.sessions = sessions;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<GetCashSessionSummaryResponse>> ExecuteAsync(GetCashSessionSummaryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireSignedIn(session);
        if (denied is not null)
        {
            return Result<GetCashSessionSummaryResponse>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var summary = await sessions.FindSummaryAsync(unit, request.CashSessionId, cancellationToken);

        if (summary is null)
        {
            return Result<GetCashSessionSummaryResponse>.Failure(new Error(ErrorCode.NotFound, "La sesión de caja no existe."));
        }

        return Result<GetCashSessionSummaryResponse>.Success(new GetCashSessionSummaryResponse(summary));
    }
}

/// <summary>
/// Opens the single cash session (JHO-38, RN-CAJ-001/003/004): only one
/// open per installation, balance in whole COP never negative, records
/// session user and local timestamp. Any signed-in role may open.
/// </summary>
public sealed class OpenCashSessionUseCase : IUseCase<OpenCashSessionRequest, OpenCashSessionResponse>
{
    private readonly ICashSessionRepository sessions;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;
    private readonly IClock clock;
    private readonly IOperationalGate gate;

    public OpenCashSessionUseCase(ICashSessionRepository sessions, IUnitOfWorkFactory units, ICurrentSession session, IClock clock, IOperationalGate gate)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(gate);
        this.sessions = sessions;
        this.units = units;
        this.session = session;
        this.clock = clock;
        this.gate = gate;
    }

    public async Task<Result<OpenCashSessionResponse>> ExecuteAsync(OpenCashSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireSignedIn(session);
        if (denied is not null)
        {
            return Result<OpenCashSessionResponse>.Failure(denied);
        }

        if (request.OpeningBalanceCop < 0)
        {
            return Result<OpenCashSessionResponse>.Failure(new Error(
                ErrorCode.Validation, "El saldo inicial no puede ser negativo.", "OpeningBalanceCop"));
        }

        // P-DD §11.3: re-check the licensing/clock decision before opening cash.
        var blocked = await gate.CheckAsync(cancellationToken);
        if (blocked is not null)
        {
            return Result<OpenCashSessionResponse>.Failure(blocked);
        }

        // BEGIN IMMEDIATE: the check-and-insert must be atomic against
        // another opener (RN-CAJ-001; the unique partial index backstops).
        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        if (await sessions.FindOpenAsync(unit, cancellationToken) is not null)
        {
            return Result<OpenCashSessionResponse>.Failure(new Error(
                ErrorCode.Conflict, "Ya hay una caja abierta. Ciérrela antes de abrir otra."));
        }

        var now = clock.UtcNow;
        var id = await sessions.OpenAsync(
            unit,
            now.ToUnixTimeMilliseconds(),
            (int)now.Offset.TotalMinutes,
            session.Current!.UserId,
            session.Current.DisplayName,
            request.OpeningBalanceCop,
            cancellationToken);

        await unit.CommitAsync(cancellationToken);
        return Result<OpenCashSessionResponse>.Success(new OpenCashSessionResponse(id));
    }
}

/// <summary>
/// Current open session with running totals (JHO-38). <c>null</c> inside
/// the response when none is open — the TPV keeps drafting regardless
/// (P-UX-010). Any signed-in role may read.
/// </summary>
public sealed class GetOpenCashSessionUseCase : IUseCase<GetOpenCashSessionRequest, GetOpenCashSessionResponse>
{
    private readonly ICashSessionRepository sessions;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public GetOpenCashSessionUseCase(ICashSessionRepository sessions, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.sessions = sessions;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<GetOpenCashSessionResponse>> ExecuteAsync(GetOpenCashSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireSignedIn(session);
        if (denied is not null)
        {
            return Result<GetOpenCashSessionResponse>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var open = await sessions.FindOpenAsync(unit, cancellationToken);
        return Result<GetOpenCashSessionResponse>.Success(new GetOpenCashSessionResponse(open));
    }
}

/// <summary>
/// Opening-balance correction (JHO-61), regla acordada con el dueño
/// (2026-10-01): solo ADMIN, solo mientras la caja siga abierta y SIN
/// ventas registradas — si ya vendió, el camino es cerrar y reabrir.
/// Un WRITE IMMEDIATE evita corregir una caja ya cerrada.
/// </summary>
public sealed class CorrectOpeningBalanceUseCase : IUseCase<CorrectOpeningBalanceRequest, CorrectOpeningBalanceResponse>
{
    private readonly ICashSessionRepository sessions;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public CorrectOpeningBalanceUseCase(ICashSessionRepository sessions, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.sessions = sessions;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<CorrectOpeningBalanceResponse>> ExecuteAsync(CorrectOpeningBalanceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<CorrectOpeningBalanceResponse>.Failure(denied);
        }

        if (request.NewBalanceCop < 0)
        {
            return Result<CorrectOpeningBalanceResponse>.Failure(new Error(
                ErrorCode.Validation, "El saldo inicial no puede ser negativo.", "OpeningBalanceCop"));
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        var open = await sessions.FindOpenAsync(unit, cancellationToken);
        if (open is null)
        {
            return Result<CorrectOpeningBalanceResponse>.Failure(new Error(
                ErrorCode.Conflict, "No hay una caja abierta para corregir."));
        }

        if (open.SaleCount > 0)
        {
            return Result<CorrectOpeningBalanceResponse>.Failure(new Error(
                ErrorCode.Conflict, "La caja ya tiene ventas registradas. Ciérrela y abra una nueva con el saldo correcto."));
        }

        var corrected = await sessions.CorrectOpeningBalanceAsync(unit, open.CashSessionId, request.NewBalanceCop, cancellationToken);
        if (!corrected)
        {
            return Result<CorrectOpeningBalanceResponse>.Failure(new Error(
                ErrorCode.Conflict, "La caja ya no está abierta."));
        }

        await unit.CommitAsync(cancellationToken);
        return Result<CorrectOpeningBalanceResponse>.Success(new CorrectOpeningBalanceResponse(request.NewBalanceCop));
    }
}

/// <summary>
/// Closes the open cash session (JHO-38, RN-CAJ-003/008): no counted cash,
/// no shortage/overage, no reason — totals freeze at close time
/// (RN-CAJ-010). Closing nothing open, or a session that was already
/// closed, is a business Conflict.
/// </summary>
public sealed class CloseCashSessionUseCase : IUseCase<CloseCashSessionRequest, CloseCashSessionResponse>
{
    private readonly ICashSessionRepository sessions;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;
    private readonly IClock clock;
    private readonly IOperationalGate gate;

    public CloseCashSessionUseCase(ICashSessionRepository sessions, IUnitOfWorkFactory units, ICurrentSession session, IClock clock, IOperationalGate gate)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(gate);
        this.sessions = sessions;
        this.units = units;
        this.session = session;
        this.clock = clock;
        this.gate = gate;
    }

    public async Task<Result<CloseCashSessionResponse>> ExecuteAsync(CloseCashSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireSignedIn(session);
        if (denied is not null)
        {
            return Result<CloseCashSessionResponse>.Failure(denied);
        }

        // P-DD §11.3: re-check the licensing/clock decision before closing cash.
        var blocked = await gate.CheckAsync(cancellationToken);
        if (blocked is not null)
        {
            return Result<CloseCashSessionResponse>.Failure(blocked);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);

        var open = await sessions.FindOpenAsync(unit, cancellationToken);
        if (open is null)
        {
            return Result<CloseCashSessionResponse>.Failure(new Error(
                ErrorCode.Conflict, "No hay una caja abierta para cerrar."));
        }

        var now = clock.UtcNow;
        var closed = await sessions.CloseOpenAsync(
            unit,
            now.ToUnixTimeMilliseconds(),
            (int)now.Offset.TotalMinutes,
            session.Current!.UserId,
            session.Current.DisplayName,
            cancellationToken);

        if (!closed)
        {
            return Result<CloseCashSessionResponse>.Failure(new Error(
                ErrorCode.Conflict, "La caja ya estaba cerrada."));
        }

        await unit.CommitAsync(cancellationToken);
        return Result<CloseCashSessionResponse>.Success(new CloseCashSessionResponse());
    }
}
