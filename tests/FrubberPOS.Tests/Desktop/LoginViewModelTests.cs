using FrubberPOS.Application.Authentication;
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

        await viewModel.LoginAsync();

        Assert.Equal("Usuario o PIN inválidos.", viewModel.ErrorMessage);
        Assert.Equal(string.Empty, viewModel.Pin);
        Assert.Equal(2, viewModel.SelectedUser!.UserId);
        Assert.Equal(0, navigator.ShellCount);
    }

    [Fact]
    public async Task CorrectPin_NavigatesToShell()
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

        await viewModel.LoginAsync();

        Assert.Equal(string.Empty, viewModel.ErrorMessage);
        Assert.Equal(1, navigator.ShellCount);
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

    private static LoginViewModel Build(AuthenticationTestBed bed, FakeNavigator navigator) =>
        new(
            new ListActiveUsersUseCase(bed.Users, bed.Units),
            new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units),
            navigator);
}
