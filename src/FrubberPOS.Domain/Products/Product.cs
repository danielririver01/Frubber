namespace FrubberPOS.Domain.Products;

/// <summary>
/// Sellable product without categories, cost or separate taxes (JHO-34):
/// unique PLU, name, unit, price in whole COP, stock in thousandths, and
/// state. <see cref="Create"/> enforces the creation invariants (mirroring
/// 001_Initial.sql); <see cref="Rehydrate"/> rebuilds stored rows as-is for
/// repositories. No floats anywhere (ADR-009). Unit locking transitions
/// belong to JHO-31; the flag travels here for storage parity only.
/// </summary>
public sealed class Product
{
    public const int MaxPluLength = 32;
    public const int MaxNameLength = 120;

    private Product(
        int productId,
        string plu,
        string name,
        ProductUnit unit,
        long priceCop,
        long stockMilli,
        bool isActive,
        bool unitLocked)
    {
        ProductId = productId;
        Plu = plu;
        Name = name;
        Unit = unit;
        PriceCop = priceCop;
        StockMilli = stockMilli;
        IsActive = isActive;
        UnitLocked = unitLocked;
    }

    public int ProductId { get; }

    public string Plu { get; }

    public string Name { get; }

    public ProductUnit Unit { get; }

    public long PriceCop { get; }

    public long StockMilli { get; }

    public bool IsActive { get; }

    public bool UnitLocked { get; }

    public static Product Create(string plu, string name, ProductUnit unit, long priceCop, long stockMilli)
    {
        return new Product(
            0,
            RequirePlu(plu),
            RequireName(name),
            unit,
            RequirePrice(priceCop),
            RequireStock(stockMilli),
            isActive: true,
            unitLocked: false);
    }

    public static Product Rehydrate(
        int productId,
        string plu,
        string name,
        ProductUnit unit,
        long priceCop,
        long stockMilli,
        bool isActive,
        bool unitLocked) =>
        new(productId, plu, name, unit, priceCop, stockMilli, isActive, unitLocked);

    private static string RequirePlu(string plu)
    {
        if (string.IsNullOrWhiteSpace(plu))
        {
            throw new ArgumentException("El PLU no puede estar vacío.", nameof(plu));
        }

        if (plu.Length > MaxPluLength || plu != plu.Trim() || plu != plu.ToUpperInvariant() || !IsPluAlphabet(plu))
        {
            throw new ArgumentException("El PLU debe ser mayúsculas A-Z y 0-9, sin espacios, máximo 32 caracteres.", nameof(plu));
        }

        return plu;
    }

    private static bool IsPluAlphabet(string plu)
    {
        foreach (var c in plu)
        {
            var upper = c is >= 'A' and <= 'Z';
            var digit = c is >= '0' and <= '9';
            if (!upper && !digit)
            {
                return false;
            }
        }

        return true;
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre no puede estar vacío.", nameof(name));
        }

        if (name.Length > MaxNameLength || name != name.Trim())
        {
            throw new ArgumentException("El nombre no puede tener espacios al inicio o fin y máximo 120 caracteres.", nameof(name));
        }

        return name;
    }

    private static long RequirePrice(long priceCop)
    {
        if (priceCop <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priceCop), "El precio debe ser mayor a cero COP.");
        }

        return priceCop;
    }

    private static long RequireStock(long stockMilli)
    {
        if (stockMilli < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stockMilli), "La existencia inicial no puede ser negativa.");
        }

        return stockMilli;
    }
}
