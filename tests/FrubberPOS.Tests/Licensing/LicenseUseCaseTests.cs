using FrubberPOS.Application.Licensing;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;

namespace FrubberPOS.Tests.Licensing;

public sealed class LicenseUseCaseTests
{
    [Fact]
    public async Task ActivateLicense_Success()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await new ActivateLicenseUseCase(bed.License, bed.MachineCode, bed.Signature)
            .ExecuteAsync(new ActivateLicenseRequest("AKL000-TEST-1234"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    [Fact]
    public async Task GetLicenseStatus_NoLicense()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var result = await new GetLicenseStatusUseCase(bed.License).ExecuteAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
    }
}
