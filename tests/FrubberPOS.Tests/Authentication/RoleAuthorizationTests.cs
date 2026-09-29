using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Users;
using FrubberPOS.Domain.Users;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Authentication;

/// <summary>
/// Proves permissions live in Application (JHO-29): every admin use case
/// denies cashiers and unsigned sessions with the same message, no matter
/// what the UI shows.
/// </summary>
public sealed class RoleAuthorizationTests
{
    [Fact]
    public async Task CashierSession_DeniesAllAdminUseCases()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var cashier = new FakeCurrentSession();
        cashier.SignIn(new SignedInUser(2, "Zed", UserRole.Cashier));

        var errors = await RunAllAdminUseCasesAsync(bed, cashier);

        Assert.Equal(7, errors.Count);
        Assert.All(errors, e => Assert.Equal(ErrorCode.Authorization, e.Code));
        Assert.All(errors, e => Assert.Equal(Authorization.DeniedMessage, e.Message));
    }

    [Fact]
    public async Task UnsignedSession_DeniesAllAdminUseCases()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();

        var errors = await RunAllAdminUseCasesAsync(bed, new FakeCurrentSession());

        Assert.Equal(7, errors.Count);
        Assert.All(errors, e => Assert.Equal(ErrorCode.Authorization, e.Code));
    }

    [Fact]
    public async Task AdminSession_PassesTheGate()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var list = new ListUsersUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession());

        var result = await list.ExecuteAsync(new ListUsersRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static async Task<List<Error>> RunAllAdminUseCasesAsync(AuthenticationTestBed bed, FakeCurrentSession session)
    {
        var errors = new List<Error>();
        var ct = CancellationToken.None;

        errors.Add((await new CreateUserUseCase(bed.Users, bed.Hasher, bed.Units, session)
            .ExecuteAsync(new CreateUserRequest("X", UserRole.Cashier, "1234"), ct)).Error!);
        errors.Add((await new UpdateUserUseCase(bed.Users, bed.Units, session)
            .ExecuteAsync(new UpdateUserRequest(2, "X", UserRole.Cashier), ct)).Error!);
        errors.Add((await new ResetUserPinUseCase(bed.Users, bed.Hasher, bed.Units, session)
            .ExecuteAsync(new ResetUserPinRequest(2, "1234"), ct)).Error!);
        errors.Add((await new SetUserActiveStateUseCase(bed.Users, bed.Units, session)
            .ExecuteAsync(new SetUserActiveStateRequest(2, true), ct)).Error!);
        errors.Add((await new DeleteUserUseCase(bed.Users, bed.Units, session)
            .ExecuteAsync(new DeleteUserRequest(2), ct)).Error!);
        errors.Add((await new ListUsersUseCase(bed.Users, bed.Units, session)
            .ExecuteAsync(new ListUsersRequest(), ct)).Error!);
        errors.Add((await new GetUserUseCase(bed.Users, bed.Units, session)
            .ExecuteAsync(new GetUserRequest(2), ct)).Error!);

        return errors;
    }
}
