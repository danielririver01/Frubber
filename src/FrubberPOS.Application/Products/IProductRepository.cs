using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Products;

/// <summary>
/// Product persistence port. Specific to products — no generic CRUD
/// (P-DD §8.2). Every method receives the caller's <see cref="IUnitOfWork"/>
/// so reads and the PLU uniqueness re-check share one connection and one
/// transaction. Consumed by use cases, never by ViewModels.
/// </summary>
public interface IProductRepository
{
    Task<ProductDto?> FindByIdAsync(IUnitOfWork unit, int productId, CancellationToken cancellationToken);

    Task<ProductDto?> FindByPluAsync(IUnitOfWork unit, string plu, CancellationToken cancellationToken);

    Task<bool> PluExistsAsync(IUnitOfWork unit, string plu, int? excludeProductId, CancellationToken cancellationToken);

    /// <summary>
    /// Matches by PLU prefix or name substring (case-insensitive). An empty
    /// term lists everything. Result is capped at 500 rows ordered by
    /// active first, then name.
    /// </summary>
    Task<IReadOnlyList<ProductDto>> SearchAsync(IUnitOfWork unit, string term, bool includeInactive, CancellationToken cancellationToken);

    Task<int> CreateAsync(
        IUnitOfWork unit,
        string plu,
        string name,
        Domain.Products.ProductUnit productUnit,
        long priceCop,
        long stockMilli,
        bool unitLocked,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        IUnitOfWork unit,
        int productId,
        string plu,
        string name,
        Domain.Products.ProductUnit productUnit,
        long priceCop,
        CancellationToken cancellationToken);

    Task SetActiveAsync(IUnitOfWork unit, int productId, bool isActive, CancellationToken cancellationToken);

    /// <summary>
    /// Direct stock replacement (RN-INV-003/004). Setting a non-zero value
    /// locks the unit (001 CHECK parity); zeroing never unlocks it (JHO-31).
    /// </summary>
    Task SetStockAsync(IUnitOfWork unit, int productId, long newStockMilli, CancellationToken cancellationToken);

    Task DeleteAsync(IUnitOfWork unit, int productId, CancellationToken cancellationToken);

    Task<bool> HasSalesHistoryAsync(IUnitOfWork unit, int productId, CancellationToken cancellationToken);
}
