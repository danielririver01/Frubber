using FrubberPOS.Application.Common;
using FrubberPOS.Domain.Products;

namespace FrubberPOS.Application.Products;

/// <summary>
/// Input normalization and validation shared by the product use cases
/// (JHO-32). Mirrors the invariants of <see cref="Product"/> and the
/// 001_Initial.sql CHECKs, but expressed as <see cref="Error"/> values so
/// business failures never travel as exceptions (P-DD-003).
/// </summary>
internal static class ProductInputs
{
    /// <summary>Approved PLU normalization: trim and uppercase.</summary>
    public static string NormalizePlu(string? plu) => plu?.Trim().ToUpperInvariant() ?? string.Empty;

    public static string NormalizeName(string? name) => name?.Trim() ?? string.Empty;

    public static Error? ValidatePlu(string plu)
    {
        if (plu.Length is < 1 or > Product.MaxPluLength)
        {
            return new Error(ErrorCode.Validation, "El PLU debe tener entre 1 y 32 caracteres.", "Plu");
        }

        foreach (var c in plu)
        {
            if (c is not (>= 'A' and <= 'Z') && c is not (>= '0' and <= '9'))
            {
                return new Error(ErrorCode.Validation, "El PLU solo admite letras A-Z y dígitos 0-9, sin espacios.", "Plu");
            }
        }

        return null;
    }

    public static Error? ValidateName(string name)
    {
        if (name.Length is < 1 or > Product.MaxNameLength)
        {
            return new Error(ErrorCode.Validation, "El nombre debe tener entre 1 y 120 caracteres.", "Name");
        }

        return null;
    }

    public static Error? ValidateUnit(ProductUnit unit)
    {
        if (!Enum.IsDefined(unit))
        {
            return new Error(ErrorCode.Validation, "La unidad debe ser KG, LB o UNIT.", "Unit");
        }

        return null;
    }

    public static Error? ValidatePrice(long priceCop)
    {
        if (priceCop <= 0)
        {
            return new Error(ErrorCode.Validation, "El precio debe ser mayor a cero COP.", "PriceCop");
        }

        return null;
    }
}
