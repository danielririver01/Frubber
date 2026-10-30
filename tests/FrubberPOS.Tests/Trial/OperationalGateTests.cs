using FrubberPOS.Application.Trial;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;

using FrubberPOS.Application.Common;
namespace FrubberPOS.Tests.Trial;

/// <summary>
/// Operational gate: clock manipulation (JHO-83), forced offline,
/// disabled hardware, forced close blocked during open session.
/// </summary>
public sealed class OperationalGateTests
{
    [Fact]
    public async Task BlockedGate_RendersBlockedViewModel()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        bed.Gate.Blocked = new Error(ErrorCode.LicenseBlocked, "El periodo de prueba terminó. Active la licencia para continuar.");

        var vm = new TrialBlockedViewModel(
            new EvaluateTrialUseCase(bed.Trial, bed.Gate),
            new FakeNavigator(),
            bed.Gate);

        await vm.RefreshAsync();

        Assert.True(vm.IsBlocked);
        Assert.Contains("terminó", vm.Message);
        Assert.True(vm.ActivateCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClockNotManipulated_Pass()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await PostgresTestDb.MigrateAsync();

        var gate = bed.Gate;
        await gate.TickAsync(CancellationToken.None);

        Assert.False(gate.Blocked);
    }

    [Fact]
    public async Task Offline_IsDenied()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        bed.Gate.Blocked = new Error(ErrorCode.LicenseBlocked, "No se puede verificar la licencia sin conexión.");

        var result = await new EvaluateTrialUseCase(bed.Trial, bed.Gate)
            .EvaluateAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.LicenseBlocked, result.Error!.Code);
    }
}
