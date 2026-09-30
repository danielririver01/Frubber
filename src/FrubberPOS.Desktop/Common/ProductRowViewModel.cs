using FrubberPOS.Application.Products;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Desktop.Common;

/// <summary>
/// Formatted presentation of a product row in the products grid (JHO-30).
/// Negative stock carries sign, color flag and an accessible text
/// alternative (P-UX-005, spec §12.1); money is whole COP (ADR-009).
/// </summary>
public sealed record ProductRowViewModel(
    ProductDto Source,
    string Plu,
    string Name,
    string UnitText,
    string PriceText,
    string StockText,
    string StockA11yText,
    bool IsNegativeStock,
    string StateText)
{
    public static ProductRowViewModel From(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var unitCode = ProductUnits.ToCode(product.Unit);
        var stockText = QuantityText.Format(product.StockMilli, product.Unit) + " " + unitCode;
        var negative = product.StockMilli < 0;

        return new ProductRowViewModel(
            product,
            product.Plu,
            product.Name,
            unitCode,
            MoneyText.FormatCop(product.PriceCop),
            stockText,
            negative ? "Existencia negativa: " + stockText : "Existencia: " + stockText,
            negative,
            product.IsActive ? "Activo" : "Inactivo");
    }

    public int ProductId => Source.ProductId;
}
