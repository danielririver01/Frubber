using System.Data.Common;

namespace FrubberPOS.Application.Persistence;

/// <summary>
/// Opens connections to the PostgreSQL server that hosts the POS database
/// (ADR-010, JHO-21). Every returned connection leaves with the session
/// policies applied (lock timeout, statement timeout) and verified. The
/// caller owns the connection: release it with <c>await using</c>. Returns
/// <see cref="DbConnection"/> (base type) so Application never depends on
/// the Npgsql provider.
/// </summary>
public interface IDatabaseConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);
}
