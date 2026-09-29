using Dapper;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Infrastructure.Persistence.Licensing;

/// <summary>
/// Single-row license state over Dapper with parameterized SQL (JHO-25).
/// Stateless over the caller's <see cref="IUnitOfWork"/> like the other
/// repositories; the row id is always 1 per 001_Initial.sql.
/// </summary>
public sealed class DapperLicenseRepository : ILicenseRepository
{
    private const int SingleRowId = 1;

    public async Task<StoredLicense?> ReadAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);

        const string sql = "SELECT PayloadVersion, ProtectedPayload, UpdatedAtUtcMs"
            + " FROM LicenseState WHERE LicenseStateId = @Id;";
        var row = await unit.Connection.QuerySingleOrDefaultAsync<LicenseRow>(
            sql, new { Id = SingleRowId }, unit.Transaction);

        return row is null ? null : new StoredLicense(row.PayloadVersion, row.ProtectedPayload, row.UpdatedAtUtcMs);
    }

    public async Task SaveAsync(IUnitOfWork unit, StoredLicense license, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(license);

        const string sql = "INSERT INTO LicenseState (LicenseStateId, PayloadVersion, ProtectedPayload, UpdatedAtUtcMs)"
            + " VALUES (@Id, @PayloadVersion, @ProtectedPayload, @UpdatedAtUtcMs)"
            + " ON CONFLICT (LicenseStateId) DO UPDATE SET"
            + " PayloadVersion = excluded.PayloadVersion,"
            + " ProtectedPayload = excluded.ProtectedPayload,"
            + " UpdatedAtUtcMs = excluded.UpdatedAtUtcMs;";
        await unit.Connection.ExecuteAsync(
            sql,
            new
            {
                Id = SingleRowId,
                license.PayloadVersion,
                license.ProtectedPayload,
                license.UpdatedAtUtcMs,
            },
            unit.Transaction);
    }

    private sealed class LicenseRow
    {
        public int PayloadVersion { get; set; }

        public byte[] ProtectedPayload { get; set; } = [];

        public long UpdatedAtUtcMs { get; set; }
    }
}
