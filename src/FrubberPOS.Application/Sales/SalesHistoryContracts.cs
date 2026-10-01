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

/// <summary>Sale history read port (JHO-47). Read-only queries only.</summary>
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
}
