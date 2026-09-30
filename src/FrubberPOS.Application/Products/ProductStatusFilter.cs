namespace FrubberPOS.Application.Products;

/// <summary>
/// State filter for product search (spec §12.1: Todos/Activos/Inactivos).
/// </summary>
public enum ProductStatusFilter
{
    Todos,
    Activos,
    Inactivos
}
