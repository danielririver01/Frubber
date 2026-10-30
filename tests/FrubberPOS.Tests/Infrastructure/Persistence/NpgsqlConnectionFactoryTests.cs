using FrubberPOS.Application.Persistence;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Tests.TestInfrastructure;
using Npgsql;

namespace FrubberPOS.Tests.Infrastructure.Persistence;

public sealed class NpgsqlConnectionFactoryTests
{
    [Fact]
    public async Task OpenConnection_VerifiesConnectionIsAlive()
    {
        var factory = new NpgsqlConnectionFactory(new DatabaseOptions(PostgresTestDb.ConnectionString));
        await using var connection = await factory.CreateOpenConnectionAsync(CancellationToken.None);

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        var factory = new NpgsqlConnectionFactory(new DatabaseOptions(PostgresTestDb.ConnectionString));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.CreateOpenConnectionAsync(canceled.Token));
    }

    [Fact]
    public void DatabaseOptions_RejectsEmptyConnectionString()
    {
        Assert.Throws<ArgumentException>(() => new DatabaseOptions("  "));
    }

    [Fact]
    public void DatabaseOptions_RejectsNegativeTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DatabaseOptions("Host=localhost", -1));
    }

    [Fact]
    public void DatabaseOptions_DefaultBusyTimeout_IsFiveSeconds()
    {
        Assert.Equal(5000, new DatabaseOptions("Host=localhost").BusyTimeoutMilliseconds);
    }
}
