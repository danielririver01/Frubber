using Dapper;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Trial;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Tests.Authentication;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

public sealed class LoginViewModelTests
{
    [Fact]
    public async Task LoadUsers_PopulatesList()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());

        await viewModel.LoadUsersAsync();

        Assert.Equal(3, viewModel.Users.Count);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task EnterDigit_AppendsUpToFour()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());

        viewModel.EnterDigit("1");
        viewModel.EnterDigit("2");
        viewModel.EnterDigit("a");
        viewModel.EnterDigit(null);
        viewModel.EnterDigit("12");

        Assert.Equal("12", viewModel.Pin);
        Assert.Equal("●●○○", viewModel.PinMask);

        viewModel.EnterDigit("3");
        viewModel.EnterDigit("4");
        viewModel.EnterDigit("5");

        Assert.Equal("1234", viewModel.Pin);
        Assert.Equal("●●●●", viewModel.PinMask);
    }

    [Fact]
    public async Task Backspace_RemovesLast()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());

        viewModel.Backspace();

        viewModel.EnterDigit("1");
        viewModel.EnterDigit("2");
        viewModel.Backspace();

        Assert.Equal("1", viewModel.Pin);

        viewModel.Backspace();
        viewModel.Backspace();

        Assert.Equal(string.Empty, viewModel.Pin);
    }

    [Fact]
    public async Task SelectUser_ClearsPin()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();

        viewModel.EnterDigit("9");
        viewModel.SelectedUser = viewModel.Users.Single(u => u.UserId == 2);

        Assert.Equal(string.Empty, viewModel.Pin);
    }

    [Fact]
    public async Task WrongPin_ShowsErrorClearsPinKeepsSelectionAndStays()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var navigator = new FakeNavigator();
        var viewModel = Build(bed, navigator);
        await viewModel.LoadUsersAsync();

        viewModel.SelectedUser = viewModel.Users.Single(u => u.UserId == 2);
        foreach (var digit in new[] { "9", "9", "9", "9" })
        {
            viewModel.EnterDigit(digit);
        }

        await WaitForAsync(() => !string.IsNullOrEmpty(viewModel.ErrorMessage));

        Assert.Equal("Usuario o PIN inválidos.", viewModel.ErrorMessage);
        Assert.Equal(string.Empty, viewModel.Pin);
        Assert.Equal(2, viewModel.SelectedUser!.UserId);
        Assert.Equal(0, navigator.ShellCount);
    }

    [Fact]
    public async Task CorrectPin_EntersAutomaticallyOnFourthDigit()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var navigator = new FakeNavigator();
        var viewModel = Build(bed, navigator);
        await viewModel.LoadUsersAsync();

        viewModel.SelectedUser = viewModel.Users.Single(u => u.UserId == 2);
        foreach (var digit in new[] { "2", "2", "2", "2" })
        {
            viewModel.EnterDigit(digit);
        }

        // Sin Enter ni botón: el cuarto dígito intenta entrar directo.
        await WaitForAsync(() => navigator.ShellCount == 1);

        Assert.Equal(string.Empty, viewModel.ErrorMessage);
        Assert.Equal(2, navigator.LastShellUser!.UserId);
    }

    [Fact]
    public async Task NoSelection_ShowsMessage()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var navigator = new FakeNavigator();
        var viewModel = Build(bed, navigator);

        await viewModel.LoginAsync();

        Assert.Equal("Seleccione un usuario.", viewModel.ErrorMessage);
        Assert.Equal(0, navigator.ShellCount);
    }

    [Fact]
    public async Task ShowLicense_OpensLicenseDialog()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var navigator = new FakeNavigator();
        var viewModel = Build(bed, navigator);

        viewModel.ShowLicenseCommand.Execute(null);

        Assert.Equal(1, navigator.LicenseCount);
    }

    [Fact]
    public async Task TrialLine_FreshTrial_ShowsThirtyDays()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());

        await viewModel.LoadTrialAsync();

        Assert.Equal("Versión de prueba — quedan 30 días.", viewModel.TrialLine);
    }

    [Fact]
    public async Task SingleActiveUser_IsPreselected()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        // Dejar un solo usuario activo (escenario "primer administrador").
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("UPDATE \"User\" SET IsActive = 0 WHERE UserId IN (2, 3);");
        }

        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();

        Assert.Single(viewModel.Users);
        Assert.NotNull(viewModel.SelectedUser);
        Assert.Equal(1, viewModel.SelectedUser!.UserId);
        Assert.Equal("Admin · Administrador", viewModel.SelectedUserLabel);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "La condición no se cumplió en 5 segundos.");
    }

    private static LoginViewModel Build(AuthenticationTestBed bed, FakeNavigator navigator) =>
        new(
            new ListActiveUsersUseCase(bed.Users, bed.Units),
            new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units),
            new EvaluateTrialUseCase(
                new GetLicenseStatusUseCase(
                    new FakeMachineCodeProvider("0123456789ABCDEF"),
                    new FakeLicenseProtector(),
                    new FakeLicenseRepository(),
                    new FakeUnitOfWorkFactory()),
                new FakeTrialRepository(),
                new FakeUnitOfWorkFactory(),
                new FakeClock(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero))),
            navigator);
}
