using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Sales;

/// <summary>
/// One history row (RN-HIS-001): number, local date/time (UTC instant +
/// stored offset), cashier, payment method, total and sale state
/// ("ACTIVE"/"VOIDED"). Money stays in whole COP (ADR-009).
/// </summary>
public sealed record SaleHistoryItemDto(
    long SaleNumber,
    long SoldAtUtcMs,
    int SoldAtOffsetMinutes,
    string CashierName,
    string PaymentMethod,
    long TotalCop,
    string Status);

/// <summary>Paginated history page (JHO-47): newest first.</summary>
public sealed record SaleHistoryPage(
    IReadOnlyList<SaleHistoryItemDto> Items,
    int TotalCount);

/// <summary>
/// One historical detail line (RN-HIS-002): PLU, name, unit, quantity,
/// price and amount frozen at sale time — never the current catalog.
/// </summary>
public sealed record SaleDetailLineDto(
    int LineNumber,
    string Plu,
    string Name,
    string UnitCode,
    long QuantityMilli,
    long UnitPriceCop,
    long LineTotalCop);

/// <summary>
/// Full sale detail as it happened (JHO-48, RN-HIS-001/002): financial
/// header, cash session, cashier, lines and payment. Voided sales carry
/// the administrator and the void moment. Read-only by construction.
/// </summary>
public sealed record SaleDetailDto(
    long SaleNumber,
    int CashSessionId,
    string CashierName,
    long SoldAtUtcMs,
    int SoldAtOffsetMinutes,
    long SubtotalCop,
    long DiscountCop,
    long TotalCop,
    string PaymentMethod,
    long? ReceivedCop,
    long? ChangeCop,
    string Status,
    long? VoidedAtUtcMs,
    int? VoidedAtOffsetMinutes,
    string? VoidedByAdminName,
    IReadOnlyList<SaleDetailLineDto> Lines);

/// <summary>Sale history read port (JHO-47/48). Read-only queries only.</summary>
public interface ISaleHistoryRepository
{
    /// <summary>
    /// Newest-first page over the UTC range [fromUtcMs, toUtcMsExclusive).
    /// </summary>
    Task<SaleHistoryPage> SearchAsync(
        IUnitOfWork unit,
        long fromUtcMs,
        long toUtcMsExclusive,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>Full historical detail; <c>null</c> when the number is unknown.</summary>
    Task<SaleDetailDto?> FindDetailAsync(IUnitOfWork unit, long saleNumber, CancellationToken cancellationToken);
}
