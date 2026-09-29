using FrubberPOS.Application.Persistence;
using FrubberPOS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Infrastructure.Persistence;

public sealed class SqliteConnectionFactoryTests
{
    [Fact]
    public async Task OpenConnection_AppliesRequiredPragmas()
    {
        var path = NewTempDbPath();
        try
        {
            var factory = new SqliteConnectionFactory(new DatabaseOptions(path));
            await using var connection = await factory.CreateOpenConnectionAsync(CancellationToken.None);

            Assert.Equal("wal", await QuerySingleAsync(connection, "PRAGMA journal_mode;"), StringComparer.OrdinalIgnoreCase);
            Assert.Equal("2", await QuerySingleAsync(connection, "PRAGMA synchronous;"));
            Assert.Equal("1", await QuerySingleAsync(connection, "PRAGMA foreign_keys;"));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task OpenConnection_IsFileBasedEmbedded()
    {
        var path = NewTempDbPath();
        try
        {
            var factory = new SqliteConnectionFactory(new DatabaseOptions(path));
            await using var connection = await factory.CreateOpenConnectionAsync(CancellationToken.None);

            Assert.IsType<SqliteConnection>(connection);
            Assert.Equal(path, new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task DisposedConnection_ReleasesFile()
    {
        var path = NewTempDbPath();
        var factory = new SqliteConnectionFactory(new DatabaseOptions(path));
        var connection = await factory.CreateOpenConnectionAsync(CancellationToken.None);
        await connection.DisposeAsync();

        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        DeleteTempDb(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        var factory = new SqliteConnectionFactory(new DatabaseOptions(NewTempDbPath()));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.CreateOpenConnectionAsync(canceled.Token));
    }

    [Fact]
    public void DatabaseOptions_RejectsEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => new DatabaseOptions("  "));
    }

    [Fact]
    public void DatabaseOptions_RejectsNegativeTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DatabaseOptions(NewTempDbPath(), -1));
    }

    [Fact]
    public void DatabaseOptions_DefaultBusyTimeout_IsFiveSeconds()
    {
        Assert.Equal(5000, new DatabaseOptions(NewTempDbPath()).BusyTimeoutMilliseconds);
    }

    private static string NewTempDbPath() =>
        Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteTempDb(string path)
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
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

    private static async Task<string?> QuerySingleAsync(System.Data.Common.DbConnection connection, string pragma)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = pragma;
        return (await command.ExecuteScalarAsync())?.ToString();
    }
}
