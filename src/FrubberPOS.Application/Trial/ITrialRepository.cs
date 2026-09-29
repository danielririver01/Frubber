using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Trial;

/// <summary>
/// Single-row trial state (table TrialState, id always 1): first recorded
/// run and last observed instant, both Unix UTC milliseconds.
/// </summary>
public sealed record StoredTrial(long FirstRunUtcMs, long LastSeenUtcMs);

/// <summary>
/// Trial persistence over the caller's unit of work (JHO-27).
/// </summary>
public interface ITrialRepository
{
    Task<StoredTrial?> ReadAsync(IUnitOfWork unit, CancellationToken cancellationToken);

    Task SaveAsync(IUnitOfWork unit, StoredTrial trial, CancellationToken cancellationToken);
}
