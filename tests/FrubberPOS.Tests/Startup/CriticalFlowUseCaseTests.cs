using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Startup;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;

using FrubberPOS.Application.Common;
namespace FrubberPOS.Tests.Startup;

public sealed class CriticalFlowUseCaseTests
{
    [Fact]
    public async Task CheckFirstStart_NoAdmin_YieldsCheckRequired()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await new CheckFirstStartUseCase(bed.Users).ExecuteAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsFirstStart);
    }

    [Fact]
    public async Task AuthenticateUser_AgainstNoUsers_YieldsNotFound()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session)
            .ExecuteAsync(new AuthenticateUserRequest("Admin", "1234"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
    }
}
