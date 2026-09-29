using System.Data.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Infrastructure.Persistence;

internal sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly DbConnection connection;
    private readonly DbTransaction? transaction;
    private readonly bool immediate;
    private bool committed;
    private bool disposed;

    private SqliteUnitOfWork(DbConnection connection, DbTransaction? transaction, bool immediate)
    {
        this.connection = connection;
        this.transaction = transaction;
        this.immediate = immediate;
    }

    internal static async Task<SqliteUnitOfWork> BeginAsync(DbConnection connection, UnitOfWorkMode mode, CancellationToken cancellationToken)
    {
        DbTransaction? transaction = null;
        var immediate = false;

        if (mode == UnitOfWorkMode.WriteImmediate)
        {
            await using var begin = connection.CreateCommand();
            begin.CommandText = "BEGIN IMMEDIATE;";
            await begin.ExecuteNonQueryAsync(cancellationToken);
            immediate = true;
        }
        else if (mode == UnitOfWorkMode.Write)
        {
            transaction = connection.BeginTransaction();
        }

        return new SqliteUnitOfWork(connection, transaction, immediate);
    }

    public DbConnection Connection
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return connection;
        }
    }

    public DbTransaction? Transaction
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return transaction;
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (committed)
        {
            throw new InvalidOperationException("La unidad de trabajo ya fue confirmada.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (immediate)
        {
            await using var commit = connection.CreateCommand();
            commit.CommandText = "COMMIT;";
            await commit.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            transaction?.Commit();
        }

        committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (!committed)
        {
            // Rollback on dispose must never mask the original failure, so a
            // failing ROLLBACK is intentionally best-effort here.
            try
            {
                if (immediate)
                {
                    await using var rollback = connection.CreateCommand();
                    rollback.CommandText = "ROLLBACK;";
                    await rollback.ExecuteNonQueryAsync();
                }
                else if (transaction is not null)
                {
                    await transaction.RollbackAsync();
                }
            }
            catch
            {
                // Best-effort: the unit is discarded either way.
            }
        }

        if (transaction is not null)
        {
            await transaction.DisposeAsync();
        }

        await connection.DisposeAsync();
    }
}
