using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.System;

namespace FrubberPOS.Application.Cash;

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

    public OpenCashSessionUseCase(ICashSessionRepository sessions, IUnitOfWorkFactory units, ICurrentSession session, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        this.sessions = sessions;
        this.units = units;
        this.session = session;
        this.clock = clock;
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

    public CloseCashSessionUseCase(ICashSessionRepository sessions, IUnitOfWorkFactory units, ICurrentSession session, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        this.sessions = sessions;
        this.units = units;
        this.session = session;
        this.clock = clock;
    }

    public async Task<Result<CloseCashSessionResponse>> ExecuteAsync(CloseCashSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireSignedIn(session);
        if (denied is not null)
        {
            return Result<CloseCashSessionResponse>.Failure(denied);
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
