using System.Data.Common;

namespace FrubberPOS.Application.Persistence;

/// <summary>
/// Opens configured PostgreSQL connections (engine on the local network,
/// ADR of Movimiento 2). Every returned connection leaves with the session
/// policies applied and verified (lock_timeout from the busy timeout,
/// bounded pool size per puesto). The caller owns the connection: release
/// it with <c>await using</c>. Returns <see cref="DbConnection"/> (base
/// type) so Application never depends on the Npgsql provider.
/// </summary>
public interface IDatabaseConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);
}
