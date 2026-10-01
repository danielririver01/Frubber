using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Cash;

/// <summary>
/// Read model of the open cash session with its running totals (JHO-38,
/// RN-CAJ-005/006/007): cash and transfer sales separate, expected cash =
/// opening balance + valid cash sales. Money stays whole COP (ADR-009).
/// </summary>
public sealed record OpenCashSessionDto(
    int CashSessionId,
    long OpenedAtUtcMs,
    int OpenedAtOffsetMinutes,
    string OpenedByNameSnapshot,
    long OpeningBalanceCop,
    long CashSalesCop,
    long TransferSalesCop,
    long DiscountCop,
    int SaleCount)
{
    public long NetSalesCop => CashSalesCop + TransferSalesCop;

    public long ExpectedCashCop => OpeningBalanceCop + CashSalesCop;
}

/// <summary>Cash persistence port (P-DD §8.2). Single open session per installation.</summary>
public interface ICashSessionRepository
{
    Task<OpenCashSessionDto?> FindOpenAsync(IUnitOfWork unit, CancellationToken cancellationToken);

    Task<int> OpenAsync(
        IUnitOfWork unit,
        long openedAtUtcMs,
        int openedAtOffsetMinutes,
        int openedByUserId,
        string openedByNameSnapshot,
        long openingBalanceCop,
        CancellationToken cancellationToken);

    /// <summary>Closes the open session freezing its totals. <c>false</c> when nothing was open.</summary>
    Task<bool> CloseOpenAsync(
        IUnitOfWork unit,
        long closedAtUtcMs,
        int closedAtOffsetMinutes,
        int closedByUserId,
        string closedByNameSnapshot,
        CancellationToken cancellationToken);

    /// <summary>
    /// Financial summary of a session (JHO-35). For the open session the
    /// totals compute from valid sales; for a closed one they come frozen
    /// from the row. <c>null</c> when the id is unknown.
    /// </summary>
    Task<CashSessionSummaryDto?> FindSummaryAsync(IUnitOfWork unit, int cashSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Corrects the opening balance of the open session (JHO-61).
    /// <c>false</c> when the session closed meanwhile.
    /// </summary>
    Task<bool> CorrectOpeningBalanceAsync(IUnitOfWork unit, int cashSessionId, long openingBalanceCop, CancellationToken cancellationToken);
}

/// <summary>
/// Session financial summary (JHO-35, RN-CAJ-005/006/007/010): cash and
/// transfer split, discounts, net sales, expected cash and sale count in
/// whole COP (ADR-009). Voided sales never inflate the numbers.
/// </summary>
public sealed record CashSessionSummaryDto(
    int CashSessionId,
    long OpenedAtUtcMs,
    int OpenedAtOffsetMinutes,
    string OpenedByNameSnapshot,
    long OpeningBalanceCop,
    bool IsClosed,
    long? ClosedAtUtcMs,
    int? ClosedAtOffsetMinutes,
    string? ClosedByNameSnapshot,
    long CashSalesCop,
    long TransferSalesCop,
    long DiscountCop,
    long NetSalesCop,
    long ExpectedCashCop,
    int SaleCount);

public sealed record OpenCashSessionRequest(long OpeningBalanceCop);

public sealed record OpenCashSessionResponse(int CashSessionId);

public sealed record GetOpenCashSessionRequest;

public sealed record GetOpenCashSessionResponse(OpenCashSessionDto? Session);

public sealed record CloseCashSessionRequest;

public sealed record CloseCashSessionResponse;

public sealed record GetCashSessionSummaryRequest(int CashSessionId);

public sealed record GetCashSessionSummaryResponse(CashSessionSummaryDto Summary);

public sealed record CorrectOpeningBalanceRequest(long NewBalanceCop);

public sealed record CorrectOpeningBalanceResponse(long OpeningBalanceCop);
