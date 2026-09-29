namespace FrubberPOS.Domain.Products;

/// <summary>
/// Quantity rules over thousandths (JHO-34, ADR-009): money and stock never
/// use floating point. KG/LB accept any positive thousandth (up to three
/// decimals); UNIT accepts whole pieces only, mirroring the database CHECKs.
/// </summary>
public static class ProductQuantities
{
    public const long Scale = 1000L;

    /// <summary>
    /// Whether a sale/adjustment quantity is well-formed for the unit:
    /// positive, and whole pieces for <see cref="ProductUnit.Unit"/>.
    /// </summary>
    public static bool IsValidQuantity(ProductUnit unit, long quantityMilli)
    {
        if (quantityMilli <= 0)
        {
            return false;
        }

        return unit != ProductUnit.Unit || quantityMilli % Scale == 0;
    }

    public static bool IsWholeUnits(long quantityMilli) => quantityMilli % Scale == 0;
}
