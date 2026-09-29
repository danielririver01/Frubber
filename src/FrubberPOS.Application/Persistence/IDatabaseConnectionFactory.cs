using System.Data.Common;

namespace FrubberPOS.Application.Persistence;

/// <summary>
/// Opens configured file-based SQLite connections (embedded engine, no
/// server). Every returned connection leaves with the ADR-010 policies
/// applied and verified (foreign keys, WAL, synchronous FULL, busy
/// timeout). The caller owns the connection: release it with
/// <c>await using</c>. Returns <see cref="DbConnection"/> (base type) so
/// Application never depends on the SQLite provider.
/// </summary>
public interface IDatabaseConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);
}
