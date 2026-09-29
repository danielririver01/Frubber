using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Startup;
using FrubberPOS.Domain.Users;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Users;
using FrubberPOS.Infrastructure.Security;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Startup;

public sealed class FirstStartUseCaseTests
{
    [Fact]
    public async Task EmptyDb_CreatesSoleActiveAdminWhoCanLogIn()
    {
        var bed = await CreateBedAsync();
        try
        {
            var first = new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units);

            var created = await first.ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None);

            Assert.True(created.IsSuccess);
            var login = new AuthenticateUserUseCase(bed.Users, bed.Hasher, new FakeCurrentSession(), bed.Units);
            var auth = await login.ExecuteAsync(new AuthenticateUserRequest(created.Value.UserId, "1234"), CancellationToken.None);
            Assert.True(auth.IsSuccess);
            Assert.Equal(UserRole.Admin, auth.Value.Role);
        }
        finally
        {
            await bed.DisposeAsync();
        }
    }

    [Fact]
    public async Task EmptyDb_HasNoDefaultPin()
    {
        var bed = await CreateBedAsync();
        try
        {
            await using var unit = await bed.Units.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);

            Assert.Equal(0, await bed.Users.CountAllAsync(unit, CancellationToken.None));
            Assert.Empty(await bed.Users.ListActiveAsync(unit, CancellationToken.None));
        }
        finally
        {
            await bed.DisposeAsync();
        }
    }

    [Fact]
    public async Task NonEmptyDb_Conflict()
    {
        var bed = await CreateBedAsync();
        try
        {
            var first = new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units);
            Assert.True((await first.ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None)).IsSuccess);

            var second = await first.ExecuteAsync(new CreateFirstAdministratorRequest("Otro", "5678"), CancellationToken.None);

            Assert.True(second.IsFailure);
            Assert.Equal(ErrorCode.Conflict, second.Error!.Code);
        }
        finally
        {
            await bed.DisposeAsync();
        }
    }

    [Fact]
    public async Task RepeatCall_KeepsSingleUser()
    {
        var bed = await CreateBedAsync();
        try
        {
            var first = new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units);

            Assert.True((await first.ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None)).IsSuccess);
            Assert.True((await first.ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None)).IsFailure);

            await using var unit = await bed.Units.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
            Assert.Equal(1, await bed.Users.CountAllAsync(unit, CancellationToken.None));
        }
        finally
        {
            await bed.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("", "1234", "DisplayName")]
    [InlineData("   ", "1234", "DisplayName")]
    [InlineData("Dueño", "123", "Pin")]
    [InlineData("Dueño", "12a4", "Pin")]
    public async Task BadInput_Validation(string name, string pin, string field)
    {
        var bed = await CreateBedAsync();
        try
        {
            var first = new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units);

            var result = await first.ExecuteAsync(new CreateFirstAdministratorRequest(name, pin), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCode.Validation, result.Error!.Code);
            Assert.Equal(field, result.Error.Field);
        }
        finally
        {
            await bed.DisposeAsync();
        }
    }

    [Fact]
    public async Task CheckFirstStart_TrueThenFalse()
    {
        var bed = await CreateBedAsync();
        try
        {
            var check = new CheckFirstStartUseCase(bed.Users, bed.Units);

            var before = await check.ExecuteAsync(new CheckFirstStartRequest(), CancellationToken.None);
            Assert.True(before.IsSuccess);
            Assert.True(before.Value.IsFirstStart);

            var first = new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units);
            Assert.True((await first.ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None)).IsSuccess);

            var after = await check.ExecuteAsync(new CheckFirstStartRequest(), CancellationToken.None);
            Assert.True(after.IsSuccess);
            Assert.False(after.Value.IsFirstStart);
        }
        finally
        {
            await bed.DisposeAsync();
        }
    }

    private sealed record Bed(string Path, DapperUserRepository Users, Pbkdf2PinHasher Hasher, SqliteUnitOfWorkFactory Units)
    {
        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={Path}"));
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            foreach (var journal in new[] { Path + "-wal", Path + "-shm", Path + "-journal" })
            {
                if (File.Exists(journal))
                {
                    File.Delete(journal);
                }
            }

            await Task.CompletedTask;
        }
    }

    private static async Task<Bed> CreateBedAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");
        var migrated = await new MigrationRunner().MigrateAsync($"Data Source={path}", CancellationToken.None);
        if (!migrated.IsSuccess)
        {
            throw new InvalidOperationException("No se pudo migrar la base de prueba.");
        }

        var units = new SqliteUnitOfWorkFactory(new SqliteConnectionFactory(new DatabaseOptions(path)));
        return new Bed(path, new DapperUserRepository(), new Pbkdf2PinHasher(), units);
    }
}
