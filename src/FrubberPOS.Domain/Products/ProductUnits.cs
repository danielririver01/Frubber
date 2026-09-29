namespace FrubberPOS.Domain.Products;

/// <summary>
/// Mapping between <see cref="ProductUnit"/> and its persisted codes
/// (<c>KG</c>/<c>LB</c>/<c>UNIT</c> per 001_Initial.sql).
/// </summary>
public static class ProductUnits
{
    public static string ToCode(ProductUnit unit) => unit switch
    {
        ProductUnit.Kg => "KG",
        ProductUnit.Lb => "LB",
        ProductUnit.Unit => "UNIT",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), "Unidad desconocida.")
    };

    public static ProductUnit Parse(string code) => code switch
    {
        "KG" => ProductUnit.Kg,
        "LB" => ProductUnit.Lb,
        "UNIT" => ProductUnit.Unit,
        _ => throw new InvalidOperationException($"Código de unidad desconocido en base de datos: '{code}'.")
    };
}
