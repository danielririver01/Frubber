using FrubberPOS.Domain.Products;

namespace FrubberPOS.Application.Products;

/// <summary>
/// Read model of a product for administration lists, detail and TPV
/// search (JHO-32). No secrets involved; quantities stay in thousandths
/// and money in whole COP (ADR-009).
/// </summary>
public sealed record ProductDto(
    int ProductId,
    string Plu,
    string Name,
    ProductUnit Unit,
    long PriceCop,
    long StockMilli,
    bool IsActive,
    bool UnitLocked);
