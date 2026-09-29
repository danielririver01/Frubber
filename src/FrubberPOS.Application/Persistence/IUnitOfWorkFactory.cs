namespace FrubberPOS.Application.Persistence;

/// <summary>
/// Opens units of work. The caller owns the returned unit: release it with
/// <c>await using</c> and a single <c>CommitAsync</c> on success.
/// </summary>
public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> CreateAsync(UnitOfWorkMode mode, CancellationToken cancellationToken);
}
