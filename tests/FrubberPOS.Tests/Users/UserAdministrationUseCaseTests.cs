using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Users;
using FrubberPOS.Domain.Users;
using FrubberPOS.Tests.Authentication;

namespace FrubberPOS.Tests.Users;

public sealed class UserAdministrationUseCaseTests
{
    [Fact]
    public async Task CreateUser_Valid_CreatesActiveAdminWhoCanLogIn()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var create = new CreateUserUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession());

        var created = await create.ExecuteAsync(new CreateUserRequest("Nuevo Admin", UserRole.Admin, "5678"), CancellationToken.None);

        Assert.True(created.IsSuccess);
        var login = new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units);
        var auth = await login.ExecuteAsync(new AuthenticateUserRequest(created.Value.UserId, "5678"), CancellationToken.None);
        Assert.True(auth.IsSuccess);
        Assert.Equal(UserRole.Admin, auth.Value.Role);
    }

    [Fact]
    public async Task CreateUser_TrimsName()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var create = new CreateUserUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession());

        var created = await create.ExecuteAsync(new CreateUserRequest("  Mango  ", UserRole.Cashier, "5678"), CancellationToken.None);

        Assert.True(created.IsSuccess);
        var get = new GetUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());
        var detail = await get.ExecuteAsync(new GetUserRequest(created.Value.UserId), CancellationToken.None);
        Assert.Equal("Mango", detail.Value.User.DisplayName);
    }

    [Fact]
    public async Task CreateUser_DuplicateNameCaseInsensitive_Conflict()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var create = new CreateUserUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession());

        Assert.True((await create.ExecuteAsync(new CreateUserRequest("mango", UserRole.Cashier, "5678"), CancellationToken.None)).IsSuccess);
        var duplicate = await create.ExecuteAsync(new CreateUserRequest("MANGO", UserRole.Cashier, "8765"), CancellationToken.None);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(ErrorCode.Conflict, duplicate.Error!.Code);
        Assert.Equal("DisplayName", duplicate.Error.Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateUser_BadName_Validation(string name)
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var create = new CreateUserUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await create.ExecuteAsync(new CreateUserRequest(name, UserRole.Cashier, "5678"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_LongName_Validation()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var create = new CreateUserUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await create.ExecuteAsync(new CreateUserRequest(new string('A', 61), UserRole.Cashier, "5678"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12a4")]
    public async Task CreateUser_BadPin_Validation(string pin)
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var create = new CreateUserUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await create.ExecuteAsync(new CreateUserRequest("Valido", UserRole.Cashier, pin), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal("Pin", result.Error.Field);
    }

    [Fact]
    public async Task UpdateUser_Rename_Ok()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var update = new UpdateUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await update.ExecuteAsync(new UpdateUserRequest(2, "Zed Nuevo", UserRole.Cashier), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UpdateUser_DuplicateName_Conflict()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var update = new UpdateUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await update.ExecuteAsync(new UpdateUserRequest(2, "ADMIN", UserRole.Cashier), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
    }

    [Fact]
    public async Task UpdateUser_DemoteSoleAdmin_Conflict()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var update = new UpdateUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await update.ExecuteAsync(new UpdateUserRequest(1, "Admin", UserRole.Cashier), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
    }

    [Fact]
    public async Task UpdateUser_DemoteSecondAdmin_Ok()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var create = new CreateUserUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession());
        var created = await create.ExecuteAsync(new CreateUserRequest("Segundo", UserRole.Admin, "5678"), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var update = new UpdateUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());
        var result = await update.ExecuteAsync(new UpdateUserRequest(1, "Admin", UserRole.Cashier), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UpdateUser_Unknown_NotFound()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var update = new UpdateUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await update.ExecuteAsync(new UpdateUserRequest(999, "X", UserRole.Cashier), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task DeactivateSoleAdmin_Conflict()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var active = new SetUserActiveStateUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await active.ExecuteAsync(new SetUserActiveStateRequest(1, false), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
    }

    [Fact]
    public async Task DeactivateCashier_OkAndCannotLogIn()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var active = new SetUserActiveStateUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());

        Assert.True((await active.ExecuteAsync(new SetUserActiveStateRequest(2, false), CancellationToken.None)).IsSuccess);

        var login = new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units);
        var auth = await login.ExecuteAsync(new AuthenticateUserRequest(2, "2222"), CancellationToken.None);
        Assert.True(auth.IsFailure);

        Assert.True((await active.ExecuteAsync(new SetUserActiveStateRequest(2, true), CancellationToken.None)).IsSuccess);
        var back = await login.ExecuteAsync(new AuthenticateUserRequest(2, "2222"), CancellationToken.None);
        Assert.True(back.IsSuccess);
    }

    [Fact]
    public async Task ResetPin_NewWorksOldFails()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var reset = new ResetUserPinUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession());
        var login = new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units);

        Assert.True((await reset.ExecuteAsync(new ResetUserPinRequest(2, "7890"), CancellationToken.None)).IsSuccess);

        bed.Session.SignOut();
        Assert.True((await login.ExecuteAsync(new AuthenticateUserRequest(2, "7890"), CancellationToken.None)).IsSuccess);
        bed.Session.SignOut();
        Assert.True((await login.ExecuteAsync(new AuthenticateUserRequest(2, "2222"), CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task DeleteUser_WithoutHistory_Gone()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var create = new CreateUserUseCase(bed.Users, bed.Hasher, bed.Units, AuthenticationTestBed.AdminSession());
        var created = await create.ExecuteAsync(new CreateUserRequest("Temporal", UserRole.Cashier, "5678"), CancellationToken.None);

        var delete = new DeleteUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());
        Assert.True((await delete.ExecuteAsync(new DeleteUserRequest(created.Value.UserId), CancellationToken.None)).IsSuccess);

        var get = new GetUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());
        Assert.True((await get.ExecuteAsync(new GetUserRequest(created.Value.UserId), CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task DeleteUser_WithHistory_ConflictAndStays()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        await bed.SeedCashSessionAsync(2, "Zed Cajero");

        var delete = new DeleteUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());
        var result = await delete.ExecuteAsync(new DeleteUserRequest(2), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);

        var get = new GetUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());
        Assert.True((await get.ExecuteAsync(new GetUserRequest(2), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task DeleteSoleAdmin_Conflict()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var delete = new DeleteUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await delete.ExecuteAsync(new DeleteUserRequest(1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error!.Code);
    }

    [Fact]
    public async Task ListUsers_IncludesInactiveWithoutSecrets()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var list = new ListUsersUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await list.ExecuteAsync(new ListUsersRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Users.Count);
        Assert.Contains(result.Value.Users, u => u.DisplayName == "Inactivo" && !u.IsActive);

        foreach (var property in typeof(ActiveUserDto).GetProperties().Concat(typeof(UserDetailDto).GetProperties()))
        {
            Assert.NotEqual(typeof(byte[]), property.PropertyType);
        }
    }
}
