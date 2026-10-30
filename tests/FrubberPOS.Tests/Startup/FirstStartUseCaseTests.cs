using FrubberPOS.Application.Startup;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;

using FrubberPOS.Application.Common;
namespace FrubberPOS.Tests.Startup;

public sealed class FirstStartUseCaseTests
{
    [Fact]
    public async Task CreateFirstAdministrator_Success()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units)
            .ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.NotEmpty(result.Value.UserId);
    }

    [Fact]
    public async Task CreateFirstAdministrator_WhenExists_IsConflict()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units)
            .ExecuteAsync(new CreateFirstAdministratorRequest("Dueño", "1234"), CancellationToken.None);

        var second = await new CreateFirstAdministratorUseCase(bed.Users, bed.Hasher, bed.Units)
            .ExecuteAsync(new CreateFirstAdministratorRequest("Otro", "5678"), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorCode.Conflict, second.Error!.Code);
    }
}
