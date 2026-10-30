using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Tests.TestInfrastructure;

namespace FrubberPOS.Tests.Infrastructure.Persistence;

/// <summary>
/// Proves specific repositories can share one connection and one
/// transaction through the unit of work, with parameterized Dapper SQL.
/// </summary>
public sealed class NpgsqlUnitOfWorkTests
{
    private const string CreateTable = "CREATE TABLE T_UOW (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);";
    private const string Insert = "INSERT INTO T_UOW (Id, Name) VALUES (@Id, @Name);";

    [Fact]
    public async Task Write_CommitsAcrossSharedUnit()
    {
        await PostgresTestDb.MigrateAsync();
        await using var unit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
        await unit.Connection.ExecuteAsync(CreateTable, transaction: unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);

        await using (var unit2 = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.Write, CancellationToken.None))
        {
            await unit2.Connection.ExecuteAsync(Insert, new { Id = 1, Name = "Tomate" }, unit2.Transaction);
            await unit2.Connection.ExecuteAsync(Insert, new { Id = 2, Name = "Cebolla" }, unit2.Transaction);
            await unit2.CommitAsync(CancellationToken.None);
        }

        await using var countUnit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        var count = await countUnit.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM T_UOW;", transaction: countUnit.Transaction);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task DisposeWithoutCommit_RollsBack()
    {
        await PostgresTestDb.MigrateAsync();
        await using var unit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
        await unit.Connection.ExecuteAsync(CreateTable, transaction: unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);

        await using (var unit2 = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.Write, CancellationToken.None))
        {
            await unit2.Connection.ExecuteAsync(Insert, new { Id = 1, Name = "Tomate" }, unit2.Transaction);
            // No commit: disposed without commit rolls back.
        }

        await using var countUnit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        var count = await countUnit.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM T_UOW WHERE Id = 1;", transaction: countUnit.Transaction);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task WriteImmediate_IsAtomicOnFailure()
    {
        await PostgresTestDb.MigrateAsync();
        await using var unit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
        await unit.Connection.ExecuteAsync(CreateTable, transaction: unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);

        await using (var unit2 = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.WriteImmediate, CancellationToken.None))
        {
            await unit2.Connection.ExecuteAsync(Insert, new { Id = 1, Name = "Tomate" }, unit2.Transaction);
            try
            {
                await unit2.Connection.ExecuteAsync("THIS IS NOT SQL;", transaction: unit2.Transaction);
                Assert.Fail("El SQL inválido debió fallar.");
            }
            catch (Exception)
            {
                // Expected: the whole unit must roll back below.
            }
        }

        await using var countUnit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        var count = await countUnit.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM T_UOW WHERE Id = 1;", transaction: countUnit.Transaction);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ReadOnly_HasNoTransaction()
    {
        await PostgresTestDb.MigrateAsync();
        await using var unit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
        await unit.Connection.ExecuteAsync(CreateTable, transaction: unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);

        await using var readOnly = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);

        Assert.Null(readOnly.Transaction);
        Assert.Equal(0, await readOnly.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM T_UOW;", transaction: readOnly.Transaction));
        await readOnly.CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DoubleCommit_ThrowsInvalidOperation()
    {
        await PostgresTestDb.MigrateAsync();
        await using var unit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
        await unit.CommitAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unit.CommitAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        var factory = PostgresTestDb.NewUnitFactory();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.CreateAsync(UnitOfWorkMode.Write, canceled.Token));
    }

    [Fact]
    public async Task Parameters_SurviveQuotes()
    {
        await PostgresTestDb.MigrateAsync();
        await using var unit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
        await unit.Connection.ExecuteAsync(CreateTable, transaction: unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);

        await using (var unit2 = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.Write, CancellationToken.None))
        {
            await unit2.Connection.ExecuteAsync(Insert, new { Id = 1, Name = "O'Brien" }, unit2.Transaction);
            await unit2.CommitAsync(CancellationToken.None);
        }

        await using var reader = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        var name = await reader.Connection.QuerySingleAsync<string>(
            "SELECT Name FROM T_UOW WHERE Id = @Id;", new { Id = 1 }, reader.Transaction);

        Assert.Equal("O'Brien", name);
    }
}
