using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Trial;

namespace FrubberPOS.Infrastructure.Persistence.Trial;

/// <summary>
/// Single-row trial state over Dapper with parameterized SQL (JHO-27).
/// Stateless over the caller's <see cref="IUnitOfWork"/>; the row id is
/// always 1 per 002_Trial.sql.
/// </summary>
public sealed class DapperTrialRepository : ITrialRepository
{
    private const int SingleRowId = 1;

    public async Task<StoredTrial?> ReadAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT FirstRunUtcMs, LastSeenUtcMs"
            + " FROM TrialState WHERE TrialStateId = @Id;";
        var row = await unit.Connection.QuerySingleOrDefaultAsync<TrialRow>(
            sql, new { Id = SingleRowId }, unit.Transaction);

        return row is null ? null : new StoredTrial(row.FirstRunUtcMs, row.LastSeenUtcMs);
    }

    public async Task SaveAsync(IUnitOfWork unit, StoredTrial trial, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(trial);

        const string sql = "INSERT INTO TrialState (TrialStateId, FirstRunUtcMs, LastSeenUtcMs)"
            + " VALUES (@Id, @FirstRunUtcMs, @LastSeenUtcMs)"
            + " ON CONFLICT (TrialStateId) DO UPDATE SET"
            + " FirstRunUtcMs = excluded.FirstRunUtcMs,"
            + " LastSeenUtcMs = excluded.LastSeenUtcMs;";
        await unit.Connection.ExecuteAsync(
            sql,
            new
            {
                Id = SingleRowId,
                trial.FirstRunUtcMs,
                trial.LastSeenUtcMs,
            },
            unit.Transaction);
    }

    private sealed class TrialRow
    {
        public long FirstRunUtcMs { get; set; }

        public long LastSeenUtcMs { get; set; }
    }
}
