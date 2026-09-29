using FrubberPOS.Application.Authentication;
using FrubberPOS.Domain.Users;

namespace FrubberPOS.Tests.Authentication;

public sealed class ListActiveUsersUseCaseTests
{
    [Fact]
    public async Task ReturnsOnlyActiveUsersOrderedByName()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var useCase = new ListActiveUsersUseCase(bed.Users);

        var result = await useCase.ExecuteAsync(new ListActiveUsersRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var names = result.Value.Users.Select(u => u.DisplayName).ToList();
        Assert.Equal(["Admin", "Ana Cajera", "Zed Cajero"], names);
        Assert.DoesNotContain(result.Value.Users, u => u.DisplayName == "Inactivo");
        Assert.Equal(UserRole.Admin, result.Value.Users[0].Role);
    }
}
