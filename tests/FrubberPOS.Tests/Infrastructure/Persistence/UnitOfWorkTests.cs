using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Infrastructure.Persistence;

/// <summary>
/// Proves specific repositories can share one connection and one
/// transaction through the unit of work, with parameterized Dapper SQL.
/// </summary>
public sealed class UnitOfWorkTests
{
    private const string CreateTable = "CREATE TABLE T_UOW (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);";
    private const string Insert = "INSERT INTO T_UOW (Id, Name) VALUES (@Id, @Name);";

    [Fact]
    public async Task Write_CommitsAcrossSharedUnit()
    {
        var path = NewTempDbPath();
        try
        {
            var factory = OpenFactory(path);
            await EnsureTableAsync(factory);

            await using (var unit = await factory.CreateAsync(UnitOfWorkMode.Write, CancellationToken.None))
            {
                await unit.Connection.ExecuteAsync(Insert, new { Id = 1, Name = "Tomate" }, unit.Transaction);
                await unit.Connection.ExecuteAsync(Insert, new { Id = 2, Name = "Cebolla" }, unit.Transaction);
                await unit.CommitAsync(CancellationToken.None);
            }

            Assert.Equal(2, await RowCountAsync(path));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task DisposeWithoutCommit_RollsBack()
    {
        var path = NewTempDbPath();
        try
        {
            var factory = OpenFactory(path);
            await EnsureTableAsync(factory);

            await using (var unit = await factory.CreateAsync(UnitOfWorkMode.Write, CancellationToken.None))
            {
                await unit.Connection.ExecuteAsync(Insert, new { Id = 1, Name = "Tomate" }, unit.Transaction);
            }

            Assert.Equal(0, await RowCountAsync(path));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task WriteImmediate_IsAtomicOnFailure()
    {
        var path = NewTempDbPath();
        try
        {
            var factory = OpenFactory(path);
            await EnsureTableAsync(factory);

            await using (var unit = await factory.CreateAsync(UnitOfWorkMode.WriteImmediate, CancellationToken.None))
            {
                await unit.Connection.ExecuteAsync(Insert, new { Id = 1, Name = "Tomate" }, unit.Transaction);
                try
                {
                    await unit.Connection.ExecuteAsync("THIS IS NOT SQL;", transaction: unit.Transaction);
                    Assert.Fail("El SQL inválido debió fallar.");
                }
                catch (Exception)
                {
                    // Expected: the whole unit must roll back below.
                }
            }

            Assert.Equal(0, await RowCountAsync(path));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task ReadOnly_HasNoTransaction()
    {
        var path = NewTempDbPath();
        try
        {
            var factory = OpenFactory(path);
            await EnsureTableAsync(factory);

            await using var unit = await factory.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);

            Assert.Null(unit.Transaction);
            Assert.Equal(0, await unit.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM T_UOW;", transaction: unit.Transaction));
            await unit.CommitAsync(CancellationToken.None);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task DoubleCommit_ThrowsInvalidOperation()
    {
        var path = NewTempDbPath();
        try
        {
            var factory = OpenFactory(path);
            await EnsureTableAsync(factory);

            await using var unit = await factory.CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
            await unit.CommitAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.CommitAsync(CancellationToken.None));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        var factory = OpenFactory(NewTempDbPath());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.CreateAsync(UnitOfWorkMode.Write, canceled.Token));
    }

    [Fact]
    public async Task Parameters_SurviveQuotes()
    {
        var path = NewTempDbPath();
        try
        {
            var factory = OpenFactory(path);
            await EnsureTableAsync(factory);

            await using (var unit = await factory.CreateAsync(UnitOfWorkMode.Write, CancellationToken.None))
            {
                await unit.Connection.ExecuteAsync(Insert, new { Id = 1, Name = "O'Brien" }, unit.Transaction);
                await unit.CommitAsync(CancellationToken.None);
            }

            await using var reader = await OpenFactory(path).CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
            var name = await reader.Connection.QuerySingleAsync<string>(
                "SELECT Name FROM T_UOW WHERE Id = @Id;", new { Id = 1 }, reader.Transaction);

            Assert.Equal("O'Brien", name);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    private static SqliteUnitOfWorkFactory OpenFactory(string path) =>
        new(new SqliteConnectionFactory(new DatabaseOptions(path)));

    private static async Task EnsureTableAsync(SqliteUnitOfWorkFactory factory)
    {
        await using var unit = await factory.CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
        await unit.Connection.ExecuteAsync(CreateTable, transaction: unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);
    }

    private static async Task<long> RowCountAsync(string path)
    {
        await using var unit = await OpenFactory(path).CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        return await unit.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM T_UOW;", transaction: unit.Transaction);
    }

    private static string NewTempDbPath() =>
        Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteTempDb(string path)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        foreach (var journal in new[] { path + "-wal", path + "-shm", path + "-journal" })
        {
            if (File.Exists(journal))
            {
                File.Delete(journal);
            }
        }
    }
}
