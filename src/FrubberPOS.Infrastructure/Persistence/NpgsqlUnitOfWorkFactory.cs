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

    public NpgsqlUnitOfWorkFactory(IDatabaseConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        this.connectionFactory = connectionFactory;
    }

    public async Task<IUnitOfWork> CreateAsync(UnitOfWorkMode mode, CancellationToken cancellationToken)
    {
        var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        try
        {
            return await NpgsqlUnitOfWork.BeginAsync(connection, mode, cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
