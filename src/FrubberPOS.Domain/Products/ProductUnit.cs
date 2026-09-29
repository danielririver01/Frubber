namespace FrubberPOS.Domain.Products;

/// <summary>
/// Sale unit of a product (JHO-34). Persisted as <c>KG</c>/<c>LB</c>/
/// <c>UNIT</c> per 001_Initial.sql. Weight units sell fractions;
/// <c>Unit</c> sells whole pieces only.
/// </summary>
public enum ProductUnit
{
    Kg,
    Lb,
    Unit
}
