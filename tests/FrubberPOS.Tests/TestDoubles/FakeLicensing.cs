using System.Data.Common;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Tests.TestDoubles;

/// <summary>
/// In-memory license row for ViewModel tests: ignores the unit of work.
/// Production code never uses this.
/// </summary>
public sealed class FakeLicenseRepository : ILicenseRepository
{
    private StoredLicense? stored;

    public Task<StoredLicense?> ReadAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        Task.FromResult(stored);

    public Task SaveAsync(IUnitOfWork unit, StoredLicense license, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(license);
        stored = license;
        return Task.CompletedTask;
    }
}

/// <summary>
/// No-op unit of work for tests whose repositories ignore persistence.
/// <see cref="Connection"/> is intentionally unusable: tests reaching it
/// must use the real SQLite factories instead. Production code never uses this.
/// </summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public DbConnection Connection => throw new InvalidOperationException("Esta prueba no usa conexiones reales.");

    public DbTransaction? Transaction => null;

    public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Factory of <see cref="FakeUnitOfWork"/> for persistence-free tests.
/// Production code never uses this.
/// </summary>
public sealed class FakeUnitOfWorkFactory : IUnitOfWorkFactory
{
    public Task<IUnitOfWork> CreateAsync(UnitOfWorkMode mode, CancellationToken cancellationToken) =>
        Task.FromResult<IUnitOfWork>(new FakeUnitOfWork());
}
