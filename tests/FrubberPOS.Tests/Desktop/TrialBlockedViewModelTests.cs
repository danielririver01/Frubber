using System.Text;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Trial;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Proves the block dialog explains each cause, opens activation, and only
/// lets startup continue once a license is active.
/// </summary>
public sealed class TrialBlockedViewModelTests
{
    private const string Machine = "0123456789ABCDEF";

    [Fact]
    public void Setup_Expired_ExplainsExpiryAndKeepsDataPromise()
    {
        var viewModel = Build(new FakeLicenseRepository());

        viewModel.Setup(TrialOutcome.Expired);

        Assert.Contains("30 días", viewModel.Message, StringComparison.Ordinal);
        Assert.Contains("datos están a salvo", viewModel.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Setup_ClockTampered_ExplainsClock()
    {
        var viewModel = Build(new FakeLicenseRepository());

        viewModel.Setup(TrialOutcome.ClockTampered);

        Assert.Contains("reloj", viewModel.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("datos están a salvo", viewModel.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Exit_ClosesAsRejected()
    {
        var viewModel = Build(new FakeLicenseRepository());
        bool? closed = null;
        viewModel.Closed += (_, licensed) => closed = licensed;

        viewModel.ExitCommand.Execute(null);

        Assert.Equal(false, closed);
    }

    [Fact]
    public async Task Activate_Unlicensed_DoesNotClose()
    {
        var navigator = new FakeNavigator();
        var viewModel = Build(new FakeLicenseRepository(), navigator);
        bool? closed = null;
        viewModel.Closed += (_, licensed) => closed = licensed;

        await viewModel.ActivateAsync();

        Assert.Equal(1, navigator.LicenseCount);
        Assert.Null(closed);
    }

    [Fact]
    public async Task Activate_Licensed_ClosesAsAccepted()
    {
        var repo = new FakeLicenseRepository();
        await repo.SaveAsync(
            new FakeUnitOfWork(),
            new StoredLicense(1, new FakeLicenseProtector().Protect(Encoding.UTF8.GetBytes($"FRB1|x|{Machine}|20260929")), 1),
            CancellationToken.None);
        var navigator = new FakeNavigator();
        var viewModel = Build(repo, navigator);
        bool? closed = null;
        viewModel.Closed += (_, licensed) => closed = licensed;

        await viewModel.ActivateAsync();

        Assert.Equal(1, navigator.LicenseCount);
        Assert.Equal(true, closed);
    }

    private static TrialBlockedViewModel Build(FakeLicenseRepository repo, FakeNavigator? navigator = null) =>
        new(
            new GetLicenseStatusUseCase(
                new FakeMachineCodeProvider(Machine),
                new FakeLicenseProtector(),
                repo,
                new FakeUnitOfWorkFactory()),
            navigator ?? new FakeNavigator());
}
