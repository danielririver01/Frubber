using FrubberPOS.Application.Authentication;
using FrubberPOS.Domain.Users;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Authentication;

public sealed class SignOutUseCaseTests
{
    [Fact]
    public async Task SignOut_ClearsSession()
    {
        var session = new FakeCurrentSession();
        session.SignIn(new SignedInUser(1, "Admin", UserRole.Admin));
        var useCase = new SignOutUseCase(session);

        var result = await useCase.ExecuteAsync(new SignOutRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(session.Current);
    }
}
