using System.Security.Cryptography;
using System.Text;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Licensing;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Proves the activation dialog shows the team code, rejects bad keys with
/// safe messages, and reports "Licencia vitalicia" once activated.
/// </summary>
public sealed class LicenseActivationViewModelTests
{
    private const string KeyId = "lic-test";
    private const string Machine = "0123456789ABCDEF";

    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadAsync_ShowsCodeAndUnlicensed()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (viewModel, _) = Build(Machine, ecdsa);

        await viewModel.LoadAsync();

        Assert.Equal("0123-4567-89AB-CDEF", viewModel.MachineDisplayCode);
        Assert.False(viewModel.IsLicensed);
        Assert.Equal("Sin activar", viewModel.StatusText);
        Assert.Equal("Estado: Sin activar", viewModel.StatusLine);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ActivateAsync_EmptyInput_ShowsHint()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (viewModel, repo) = Build(Machine, ecdsa);
        await viewModel.LoadAsync();
        viewModel.LicenseText = "   ";

        await viewModel.ActivateAsync();

        Assert.Equal("Pegue el código de licencia que recibió.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsLicensed);
        Assert.Null(await repo.ReadAsync(new FakeUnitOfWork(), CancellationToken.None));
    }

    [Fact]
    public async Task ActivateAsync_InvalidKey_ShowsErrorAndKeepsState()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var foreign = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (viewModel, repo) = Build(Machine, ecdsa);
        await viewModel.LoadAsync();
        viewModel.LicenseText = BuildLicenseText(Machine, new DateOnly(2026, 9, 29), foreign);

        await viewModel.ActivateAsync();

        Assert.Equal("La licencia no es válida para este equipo.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsLicensed);
        Assert.Equal("Sin activar", viewModel.StatusText);
        Assert.Null(await repo.ReadAsync(new FakeUnitOfWork(), CancellationToken.None));
    }

    [Fact]
    public async Task ActivateAsync_Valid_ShowsLifetimeAndClearsInput()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (viewModel, _) = Build(Machine, ecdsa);
        await viewModel.LoadAsync();
        viewModel.LicenseText = BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsLicensed);
        Assert.Equal("Licencia vitalicia", viewModel.StatusText);
        Assert.Equal("Estado: Licencia vitalicia", viewModel.StatusLine);
        Assert.Equal("Licencia activada correctamente.", viewModel.ConfirmationMessage);
        Assert.Equal(string.Empty, viewModel.LicenseText);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_AfterActivation_ReportsLifetime()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var repo = new FakeLicenseRepository();
        var first = Build(Machine, ecdsa, repo);
        await first.ViewModel.LoadAsync();
        first.ViewModel.LicenseText = BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa);
        await first.ViewModel.ActivateAsync();

        var second = Build(Machine, ecdsa, repo);
        await second.ViewModel.LoadAsync();

        Assert.True(second.ViewModel.IsLicensed);
        Assert.Equal("Licencia vitalicia", second.ViewModel.StatusText);
    }

    [Fact]
    public async Task CanActivate_TracksKeyShapeAndBusy()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (viewModel, _) = Build(Machine, ecdsa);
        await viewModel.LoadAsync();

        Assert.False(viewModel.CanActivate);

        viewModel.LicenseText = "parcial";
        Assert.False(viewModel.CanActivate);

        viewModel.LicenseText = BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa);
        Assert.True(viewModel.CanActivate);
    }

    [Fact]
    public async Task ActivateAsync_InvalidKey_FlagsInputErrorAndHelp()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var foreign = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (viewModel, _) = Build(Machine, ecdsa);
        await viewModel.LoadAsync();
        viewModel.LicenseText = BuildLicenseText(Machine, new DateOnly(2026, 9, 29), foreign);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.InputError);
        Assert.False(viewModel.ShowHelp);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task LicenseText_Edit_ClearsErrorState()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var foreign = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (viewModel, _) = Build(Machine, ecdsa);
        await viewModel.LoadAsync();
        viewModel.LicenseText = BuildLicenseText(Machine, new DateOnly(2026, 9, 29), foreign);
        await viewModel.ActivateAsync();

        viewModel.LicenseText = "x";

        Assert.False(viewModel.InputError);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ActivateAsync_Valid_ShowsSuccessPanelAndBusyClears()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (viewModel, _) = Build(Machine, ecdsa);
        await viewModel.LoadAsync();
        viewModel.LicenseText = BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.ShowSuccess);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.InputError);
    }

    [Fact]
    public async Task RequestClose_WithPendingCode_AskForConfirmation()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (viewModel, _) = Build(Machine, ecdsa);
        await viewModel.LoadAsync();

        Assert.True(viewModel.TryRequestClose());

        viewModel.LicenseText = "abc";
        Assert.False(viewModel.TryRequestClose());
        Assert.True(viewModel.ShowDiscardConfirm);

        viewModel.KeepEditing();
        Assert.False(viewModel.ShowDiscardConfirm);

        Assert.False(viewModel.TryRequestClose());
        viewModel.DiscardCode();
        Assert.Equal(string.Empty, viewModel.LicenseText);
        Assert.True(viewModel.TryRequestClose());
    }

    [Fact]
    public async Task NotifyCopied_SwapsLabelAndRestores()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (viewModel, _) = Build(Machine, ecdsa);
        viewModel.CopyResetMs = 30;

        Assert.Equal("Copiar", viewModel.CopyLabel);
        viewModel.NotifyCopied();
        Assert.Equal("Copiado", viewModel.CopyLabel);

        await Task.Delay(150);
        Assert.Equal("Copiar", viewModel.CopyLabel);
    }

    private static (LicenseActivationViewModel ViewModel, FakeLicenseRepository Repository) Build(
        string machine, ECDsa signer, FakeLicenseRepository? repository = null)
    {
        var repo = repository ?? new FakeLicenseRepository();
        var viewModel = new LicenseActivationViewModel(
            new GetMachineCodeUseCase(new FakeMachineCodeProvider(machine)),
            new ActivateLicenseUseCase(
                new FakeMachineCodeProvider(machine),
                new EcdsaLicenseSignatureVerifier(signer.ExportSubjectPublicKeyInfo(), KeyId),
                new FakeLicenseProtector(),
                repo,
                new FakeUnitOfWorkFactory(),
                new FakeClock(Now)),
            new GetLicenseStatusUseCase(
                new FakeMachineCodeProvider(machine),
                new FakeLicenseProtector(),
                repo,
                new FakeUnitOfWorkFactory()));
        return (viewModel, repo);
    }

    private static string BuildLicenseText(string machine, DateOnly date, ECDsa ecdsa)
    {
        var canonical = LicenseKeyFormat.BuildCanonicalPayload(KeyId, machine, date);
        var signature = ecdsa.SignData(
            Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return canonical + "." + Convert.ToBase64String(signature);
    }
}
