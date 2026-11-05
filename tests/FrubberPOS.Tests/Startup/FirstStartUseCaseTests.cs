using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Startup;
using FrubberPOS.Domain.Users;
using FrubberPOS.Infrastructure.Persistence.Users;
using FrubberPOS.Infrastructure.Security;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;

namespace FrubberPOS.Tests.Startup;

/// <summary>
/// Primer inicio contra PostgreSQL real (JHO-28, portado a JHO-113): la base
/// vacía no trae usuarios ni PIN por defecto, crea un único admin activo y
/// la validación sigue funcionando después.
/// </summary>
public sealed class FirstStartUseCaseTests
{
    [Fact]
    public async Task EmptyDb_CreatesSoleActiveAdminWhoCanLogIn()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var first = new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units);

        var created = await first.ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None);

        Assert.True(created.IsSuccess, created.Error?.Message);
        var login = new AuthenticateUserUseCase(bed.Users, bed.Hasher, new FakeCurrentSession(), bed.Units);
        var auth = await login.ExecuteAsync(new AuthenticateUserRequest(created.Value.UserId, "1234"), CancellationToken.None);
        Assert.True(auth.IsSuccess);
        Assert.Equal(UserRole.Admin, auth.Value.Role);
    }

    [Fact]
    public async Task EmptyDb_HasNoDefaultPin()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await using var unit = await bed.Units.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);

        Assert.Equal(0, await bed.Users.CountAllAsync(unit, CancellationToken.None));
        Assert.Empty(await bed.Users.ListActiveAsync(unit, CancellationToken.None));
    }

    [Fact]
    public async Task NonEmptyDb_Conflict()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var first = new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units);
        Assert.True((await first.ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None)).IsSuccess);

        var second = await first.ExecuteAsync(new CreateFirstAdministratorRequest("Otro", "5678"), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorCode.Conflict, second.Error!.Code);
    }

    [Fact]
    public async Task RepeatCall_KeepsSingleUser()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var first = new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units);

        Assert.True((await first.ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None)).IsSuccess);
        Assert.True((await first.ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None)).IsFailure);

        await using var unit = await bed.Units.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        Assert.Equal(1, await bed.Users.CountAllAsync(unit, CancellationToken.None));
    }

    [Theory]
    [InlineData("", "1234", "DisplayName")]
    [InlineData("   ", "1234", "DisplayName")]
    [InlineData("Dueño", "123", "Pin")]
    [InlineData("Dueño", "12a4", "Pin")]
    public async Task BadInput_Validation(string name, string pin, string field)
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var first = new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units);

        var result = await first.ExecuteAsync(new CreateFirstAdministratorRequest(name, pin), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal(field, result.Error.Field);
    }

    [Fact]
    public async Task CheckFirstStart_TrueThenFalse()
    {
        await using var bed = await ProductTestBed.CreateAsync();
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
}
