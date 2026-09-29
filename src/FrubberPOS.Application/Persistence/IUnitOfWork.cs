using System.Data.Common;

namespace FrubberPOS.Application.Persistence;

/// <summary>
/// Technical unit grouping repositories over a single connection and a
/// single transaction (P-DD §8.2). Use cases own it: open, work through
/// specific repositories sharing <see cref="Connection"/> (and
/// <see cref="Transaction"/> when present), then <see cref="CommitAsync"/>
/// once. Disposing without commit rolls everything back. Repositories never
/// open independent transactions inside an active unit of work.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    DbConnection Connection { get; }

    DbTransaction? Transaction { get; }

    Task CommitAsync(CancellationToken cancellationToken);
}
