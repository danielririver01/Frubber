using FrubberPOS.Application.Trial;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;

using FrubberPOS.Application.Common;
namespace FrubberPOS.Tests.Trial;

public sealed class TrialUseCaseTests
{
    [Fact]
    public async Task TrialActive_DoesNotBlock()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await new EvaluateTrialUseCase(bed.Trial, bed.Gate).EvaluateAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TrialExpired_Blocks()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        bed.Gate.Blocked = new Error(ErrorCode.LicenseBlocked, "El periodo de prueba terminó. Active la licencia para continuar.");

        var result = await new EvaluateTrialUseCase(bed.Trial, bed.Gate).EvaluateAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.LicenseBlocked, result.Error!.Code);
    }
}
