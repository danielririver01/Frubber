using FrubberPOS.Application.Common;
using FrubberPOS.Application.Products;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Application.Sales;

/// <summary>
/// Draft sale line. Quantity is always in thousandths (ADR-009).
/// Line total = (UnitPriceCop * QuantityMilli + 500) / 1000 — rounds half
/// up, mirroring CHECK on SaleLine.
/// </summary>
public sealed record DraftSaleLine(
    int ProductId,
    string Plu,
    string Name,
    ProductUnit Unit,
    long UnitPriceCop,
    long QuantityMilli)
{
    public long LineTotalCop => (UnitPriceCop * QuantityMilli + ProductQuantities.Scale / 2) / ProductQuantities.Scale;
}

/// <summary>
/// Draft sale ("en preparación") held in memory (JHO-40, RN-VEN-001/002,
/// P-UX-012/013): only active products, quantity per unit rules, repeated
/// product accumulates in one line, subtotal is money-safe integer math.
/// Persistence/finalization belongs to JHO-43.
/// </summary>
public sealed class DraftSale
{
    private readonly List<DraftSaleLine> lines = [];

    public IReadOnlyList<DraftSaleLine> Lines => lines;

    public bool IsEmpty => lines.Count == 0;

    public long DiscountCop { get; private set; }

    public long SubtotalCop => lines.Sum(l => l.LineTotalCop);

    public long TotalCop => SubtotalCop - DiscountCop;

    /// <summary>Descuento general en COP entero, entre 0 y el subtotal (JHO-41, RN-VEN).</summary>
    public Result SetDiscount(long discountCop)
    {
        if (discountCop < 0 || discountCop > SubtotalCop)
        {
            return Result.Failure(new Error(
                ErrorCode.Validation, "El descuento debe ser un valor entre cero y el subtotal.", "DiscountCop"));
        }

        DiscountCop = discountCop;
        return Result.Success();
    }

    /// <summary>
    /// Adds (or accumulates) a product line. The product must be active
    /// (RN-VEN-002) and the quantity valid for its unit (RN-VEN / JHO-34).
    /// </summary>
    public Result<DraftSaleLine> AddOrAccumulate(ProductDto product, long quantityMilli)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!product.IsActive)
        {
            return Result<DraftSaleLine>.Failure(new Error(
                ErrorCode.Validation, "El producto está inactivo y no se puede vender.", "Plu"));
        }

        if (!ProductQuantities.IsValidQuantity(product.Unit, quantityMilli))
        {
            return Result<DraftSaleLine>.Failure(new Error(
                product.Unit == ProductUnit.Unit
                    ? ErrorCode.Validation
                    : ErrorCode.Validation,
                "La cantidad no es válida para la unidad del producto.", "Quantity"));
        }

        var index = lines.FindIndex(l => l.ProductId == product.ProductId);
        DraftSaleLine updated;
        if (index >= 0)
        {
            updated = lines[index] with { QuantityMilli = lines[index].QuantityMilli + quantityMilli };
            lines[index] = updated;
        }
        else
        {
            updated = new DraftSaleLine(
                product.ProductId, product.Plu, product.Name, product.Unit, product.PriceCop, quantityMilli);
            lines.Add(updated);
        }

        return Result<DraftSaleLine>.Success(updated);
    }

    /// <summary>Replaces the quantity of an existing line. Invalid/zero qty removes the line.</summary>
    public Result RemoveOrSetQuantity(int productId, long quantityMilli)
    {
        var index = lines.FindIndex(l => l.ProductId == productId);
        if (index < 0)
        {
            return Result.Failure(new Error(ErrorCode.NotFound, "La línea no existe en la venta."));
        }

        if (quantityMilli <= 0)
        {
            lines.RemoveAt(index);
            return Result.Success();
        }

        if (!ProductQuantities.IsValidQuantity(lines[index].Unit, quantityMilli))
        {
            return Result.Failure(new Error(
                ErrorCode.Validation, "La cantidad no es válida para la unidad del producto.", "Quantity"));
        }

        lines[index] = lines[index] with { QuantityMilli = quantityMilli };
        return Result.Success();
    }

    /// <summary>Drops a line if present. No error when missing (idempotent clear).</summary>
    public void RemoveLine(int productId)
    {
        lines.RemoveAll(l => l.ProductId == productId);
        if (DiscountCop > SubtotalCop)
        {
            DiscountCop = SubtotalCop;
        }
    }

    /// <summary>Clears the draft. Caller decides when confirmation applies (P-UX-013).</summary>
    public void Clear()
    {
        lines.Clear();
        DiscountCop = 0;
    }
}
