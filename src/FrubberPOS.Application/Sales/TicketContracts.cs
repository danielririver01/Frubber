namespace FrubberPOS.Application.Sales;

/// <summary>
/// One ticket line read from the historical sale snapshots (JHO-45):
/// name, unit and price are the values frozen at sale time (RN-INV-004),
/// never the current catalog row. Quantities stay in thousandths
/// (ADR-009); money stays in whole COP.
/// </summary>
public sealed record TicketLineData(
    int LineNumber,
    string Name,
    string UnitCode,
    long QuantityMilli,
    long UnitPriceCop,
    long LineTotalCop);

/// <summary>
/// Historical sale header for the ticket (RN-IMP-004/005): number, local
/// date/time (UTC instant + stored operator offset), cashier, totals and
/// payment. <c>ReceivedCop</c>/<c>ChangeCop</c> are present only for cash.
/// </summary>
public sealed record TicketSaleData(
    long SaleNumber,
    long SoldAtUtcMs,
    int SoldAtOffsetMinutes,
    string CashierName,
    long SubtotalCop,
    long DiscountCop,
    long TotalCop,
    string PaymentMethod,
    long? ReceivedCop,
    long? ChangeCop,
    IReadOnlyList<TicketLineData> Lines);

/// <summary>
/// Composed ticket: a fixed-width plain-text document ready for the
/// Windows printer. <see cref="ConfiguredPrinterName"/> is the business
/// configuration's printer choice (may be null: none configured).
/// </summary>
public sealed record TicketDocument(
    string SaleNumberText,
    string Title,
    IReadOnlyList<string> Lines,
    string? ConfiguredPrinterName);
