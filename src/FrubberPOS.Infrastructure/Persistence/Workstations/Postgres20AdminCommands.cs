using Dapper;
using FrubberPOS.Application.Persistence;
using Npgsql;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

internal sealed class Postgres20AdminCommands(Postgres20WorkstationStore store)
{
    public async Task<T> QueryAsync<T>(IUnitOfWork unit, string sql, object? parameters,
        Func<CommandDefinition, Task<T>> query, CancellationToken cancellationToken)
    {
        var args = new DynamicParameters(parameters);
        args.Add("Token", store.AdminToken ?? throw new UnauthorizedAccessException());
        try
        {
            return await query(new CommandDefinition(sql, args, unit.Transaction, cancellationToken: cancellationToken));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw new UnauthorizedAccessException("Inicie sesión nuevamente como administrador.", ex);
        }
        catch (NpgsqlException ex) when (ex.IsTransient)
        {
            throw new DatabaseUnavailableException(ex);
        }
    }
}
