using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Infrastructure.Persistence;

/// <summary>
/// Opens <see cref="IUnitOfWork"/> over connections from
/// <see cref="IDatabaseConnectionFactory"/> (JHO-23). Owns nothing after
/// returning: the caller commits once and disposes.
/// </summary>
public sealed class NpgsqlUnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly IDatabaseConnectionFactory connectionFactory;
    private readonly bool serializeWorkstationAdmin;

    public NpgsqlUnitOfWorkFactory(IDatabaseConnectionFactory connectionFactory, bool serializeWorkstationAdmin = false)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        this.connectionFactory = connectionFactory;
        this.serializeWorkstationAdmin = serializeWorkstationAdmin;
    }

    public async Task<IUnitOfWork> CreateAsync(UnitOfWorkMode mode, CancellationToken cancellationToken)
    {
        var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        try
        {
            if (serializeWorkstationAdmin && mode == UnitOfWorkMode.WriteImmediate)
            {
                var unit = await NpgsqlUnitOfWork.BeginAsync(connection, UnitOfWorkMode.Write, cancellationToken);
                try
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = unit.Transaction;
                    command.CommandText = "SELECT pg_advisory_xact_lock(136, 13);";
                    await command.ExecuteNonQueryAsync(cancellationToken);
                    return unit;
                }
                catch
                {
                    await unit.DisposeAsync();
                    throw;
                }
            }

            return await NpgsqlUnitOfWork.BeginAsync(connection, mode, cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
