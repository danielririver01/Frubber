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
    public async Task LoadUsers_PopulatesListAndPreselectsFirst()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());

        await viewModel.LoadUsersAsync();

        Assert.Equal(3, viewModel.Users.Count);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
        Assert.Equal(1, viewModel.SelectedUser!.UserId);
        Assert.False(viewModel.IsPinPhase);
    }

    [Fact]
    public async Task EnterDigit_IgnoredInUserPhase()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();

        viewModel.EnterDigit("1");

        Assert.Equal(string.Empty, viewModel.Pin);
        Assert.False(viewModel.KeypadEnabled);
    }

    [Fact]
    public async Task EnterDigit_AppendsInPinPhase()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());
        await InPinPhaseAsync(viewModel);

        viewModel.EnterDigit("1");
        viewModel.EnterDigit("2");
        viewModel.EnterDigit("a");
        viewModel.EnterDigit(null);
        viewModel.EnterDigit("12");

        Assert.Equal("12", viewModel.Pin);
        Assert.Equal("●●○○", viewModel.PinMask);

        viewModel.EnterDigit("3");

        Assert.Equal("123", viewModel.Pin);
        Assert.Equal("●●●○", viewModel.PinMask);
        Assert.Equal("Ingresa tu PIN de 4 dígitos · faltan 1", viewModel.PinHintText);
        Assert.Equal("Ingresa tu PIN de 4 dígitos · faltan ", viewModel.PinHintLead);
        Assert.Equal("1", viewModel.PinHintStrong);
        Assert.Equal(string.Empty, viewModel.PinHintTail);
    }

    [Fact]
    public async Task Backspace_RemovesLast()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());
        await InPinPhaseAsync(viewModel);

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
        viewModel.GoToPin();

        viewModel.EnterDigit("9");
        viewModel.SelectedUser = viewModel.Users.Single(u => u.UserId == 2);

        Assert.Equal(string.Empty, viewModel.Pin);
        Assert.True(viewModel.IsPinPhase);
    }

    [Fact]
    public async Task SelectAndGoToPin_SelectsAndEntersPinPhase()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();

        viewModel.SelectAndGoToPin(viewModel.Users.Single(u => u.UserId == 3));

        Assert.Equal(3, viewModel.SelectedUser!.UserId);
        Assert.True(viewModel.IsPinPhase);
    }

    [Fact]
    public async Task WrongPin_ShowsErrorClearsPinKeepsSelectionAndStays()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var navigator = new FakeNavigator();
        var viewModel = Build(bed, navigator);
        await viewModel.LoadUsersAsync();
        viewModel.ErrorLockMs = 500;

        viewModel.SelectedUser = viewModel.Users.Single(u => u.UserId == 2);
        viewModel.GoToPin();
        foreach (var digit in new[] { "9", "9", "9", "9" })
        {
            viewModel.EnterDigit(digit);
        }

        await WaitForAsync(() => !string.IsNullOrEmpty(viewModel.ErrorMessage));

        Assert.Equal("PIN incorrecto · verifica e inténtalo de nuevo", viewModel.ErrorMessage);
        Assert.Equal(string.Empty, viewModel.Pin);
        Assert.Equal(2, viewModel.SelectedUser!.UserId);
        Assert.Equal(0, navigator.ShellCount);
    }

    [Fact]
    public async Task WrongPin_PadLockedDuringErrorWindow_ThenDigitWorksAgain()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();
        viewModel.ErrorLockMs = 2000;

        viewModel.SelectedUser = viewModel.Users.Single(u => u.UserId == 2);
        viewModel.GoToPin();
        foreach (var digit in new[] { "9", "9", "9", "9" })
        {
            viewModel.EnterDigit(digit);
        }

        await WaitForAsync(() => viewModel.PinError);

        Assert.Equal(string.Empty, viewModel.Pin);
        Assert.False(viewModel.KeypadEnabled);

        viewModel.EnterDigit("1");
        Assert.Equal(string.Empty, viewModel.Pin);

        await WaitForAsync(() => !viewModel.IsBusy);

        viewModel.EnterDigit("1");
        Assert.False(viewModel.PinError);
        Assert.Equal("Ingresa tu PIN de 4 dígitos · faltan 3", viewModel.PinHintText);
    }

    [Fact]
    public async Task CorrectPin_EntersAutomaticallyOnFourthDigit()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var navigator = new FakeNavigator();
        var viewModel = Build(bed, navigator);
        await viewModel.LoadUsersAsync();
        viewModel.OkHoldMs = 10;
        viewModel.EnteringDelayMs = 10;

        viewModel.SelectedUser = viewModel.Users.Single(u => u.UserId == 2);
        viewModel.GoToPin();
        foreach (var digit in new[] { "2", "2", "2", "2" })
        {
            viewModel.EnterDigit(digit);
        }

        // Sin Enter ni botón: el cuarto dígito intenta entrar directo.
        await WaitForAsync(() => navigator.ShellCount == 1);

        Assert.Equal(string.Empty, viewModel.ErrorMessage);
        Assert.Equal(2, navigator.LastShellUser!.UserId);
        Assert.False(viewModel.IsPinPhase);
        Assert.False(viewModel.IsEntering);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task CorrectPin_ReportsThroughOnAuthenticatedWhenSet()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var navigator = new FakeNavigator();
        var viewModel = Build(bed, navigator);
        await viewModel.LoadUsersAsync();
        viewModel.OkHoldMs = 10;
        viewModel.EnteringDelayMs = 10;

        SignedInUser? unlocked = null;
        viewModel.OnAuthenticated = user =>
        {
            unlocked = user;
            return Task.CompletedTask;
        };

        viewModel.SelectedUser = viewModel.Users.Single(u => u.UserId == 2);
        viewModel.GoToPin();
        foreach (var digit in new[] { "2", "2", "2", "2" })
        {
            viewModel.EnterDigit(digit);
        }

        await WaitForAsync(() => unlocked is not null);

        Assert.Equal(2, unlocked!.UserId);
        Assert.Equal(0, navigator.ShellCount);
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
    public async Task PinHint_FollowsPhaseAndCountsDown()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());

        Assert.Equal("Elige tu usuario y pulsa Continuar", viewModel.PinHintText);
        Assert.Equal("Elige tu usuario y pulsa ", viewModel.PinHintLead);
        Assert.Equal("Continuar", viewModel.PinHintStrong);
        Assert.Equal(string.Empty, viewModel.PinHintTail);

        await viewModel.LoadUsersAsync();
        viewModel.GoToPin();
        Assert.Equal("Ingresa tu PIN de 4 dígitos", viewModel.PinHintText);
        Assert.Equal(string.Empty, viewModel.PinHintStrong);

        viewModel.EnterDigit("1");
        Assert.Equal("Ingresa tu PIN de 4 dígitos · faltan 3", viewModel.PinHintText);
        Assert.Equal("3", viewModel.PinHintStrong);

        viewModel.EnterDigit("2");
        viewModel.EnterDigit("3");
        Assert.Equal("Ingresa tu PIN de 4 dígitos · faltan 1", viewModel.PinHintText);

        // El cuarto dígito dispara el intento; el fallo mantiene la fase PIN con el PIN limpio.
        viewModel.ErrorLockMs = 100;
        viewModel.EnterDigit("4");
        await WaitForAsync(() => !viewModel.IsBusy);
        Assert.True(viewModel.IsPinPhase);
        Assert.Equal("Ingresa tu PIN de 4 dígitos", viewModel.PinHintText);
    }

    [Fact]
    public async Task HandleEnter_UserPhase_GoesToPin_And_PinPhaseRequiresFourDigits()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();

        viewModel.HandleEnter();
        Assert.True(viewModel.IsPinPhase);

        viewModel.HandleEnter();
        Assert.Equal("Faltan 4 dígitos.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task HandleEscape_PinPhase_BackToUsers_UserPhase_ClosesApp()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();

        Assert.Equal(LoginEscapeAction.CloseApp, viewModel.HandleEscape());

        viewModel.GoToPin();
        Assert.Equal(LoginEscapeAction.None, viewModel.HandleEscape());
        Assert.False(viewModel.IsPinPhase);
    }

    [Fact]
    public async Task LockMode_EscapeNeverCloses_AndBannerHidesAfterDelay()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();
        viewModel.LockBannerMs = 50;

        viewModel.ActivateLockMode();

        Assert.True(viewModel.IsLockMode);
        Assert.True(viewModel.LockBannerVisible);
        Assert.Equal("Volver a la lista", viewModel.EscapeHintText);
        Assert.Equal(LoginEscapeAction.None, viewModel.HandleEscape());

        await WaitForAsync(() => !viewModel.LockBannerVisible);
        Assert.True(viewModel.IsLockMode);
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
        Assert.Equal("Versión de prueba — quedan 30 días.", viewModel.LicensePillText);
    }

    [Fact]
    public async Task LicensePill_WithoutTrial_ShowsSinActivar()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed, new FakeNavigator());

        Assert.Equal("Sin activar", viewModel.LicensePillText);
    }

    [Fact]
    public async Task SingleActiveUser_SkipsSelectionAndEntersPinDirectly()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        // Dejar un solo usuario activo (escenario "primer administrador").
        await DeactivateUsersAsync(bed, 2, 3);

        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();

        Assert.Single(viewModel.Users);
        Assert.NotNull(viewModel.SelectedUser);
        Assert.Equal(1, viewModel.SelectedUser!.UserId);
        Assert.Equal("Administrador", viewModel.SelectedUserLabel);
        Assert.Equal("Admin", viewModel.ContinueName);
        Assert.True(viewModel.CanContinue);
        Assert.True(viewModel.IsPinPhase);
        Assert.Equal("Salir", viewModel.EscapeHintText);
    }

    [Fact]
    public async Task HandleEscape_SingleUserInPin_ClosesAppWithoutLeavingPin()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        await DeactivateUsersAsync(bed, 2, 3);
        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();

        Assert.True(viewModel.IsPinPhase);
        Assert.Equal(LoginEscapeAction.CloseApp, viewModel.HandleEscape());
        Assert.True(viewModel.IsPinPhase);
    }

    [Fact]
    public async Task LockMode_SingleUser_StaysInPin_AndEscapeNeverCloses()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        await DeactivateUsersAsync(bed, 2, 3);
        var viewModel = Build(bed, new FakeNavigator());
        await viewModel.LoadUsersAsync();

        viewModel.ActivateLockMode();

        Assert.True(viewModel.IsLockMode);
        Assert.True(viewModel.IsPinPhase);
        Assert.Equal(LoginEscapeAction.None, viewModel.HandleEscape());
    }

    private static async Task DeactivateUsersAsync(AuthenticationTestBed bed, params int[] userIds)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        var ids = string.Join(", ", userIds);
        await connection.ExecuteAsync($"UPDATE \"User\" SET IsActive = 0 WHERE UserId IN ({ids});");
    }

    private static async Task InPinPhaseAsync(LoginViewModel viewModel)
    {
        await viewModel.LoadUsersAsync();
        viewModel.GoToPin();
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
