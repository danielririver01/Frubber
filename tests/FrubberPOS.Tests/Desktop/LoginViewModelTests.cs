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
    public async Task WrongPin_ShowsErrorClearsPinAndStays()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var navigator = new FakeNavigator();
        var viewModel = Build(bed, navigator);
        await viewModel.LoadUsersAsync();

        viewModel.SelectedUser = viewModel.Users.Single(u => u.UserId == 2);
        viewModel.Pin = "9999";
        await viewModel.LoginAsync();

        Assert.Equal("Usuario o PIN inválidos.", viewModel.ErrorMessage);
        Assert.Equal(string.Empty, viewModel.Pin);
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
        viewModel.Pin = "2222";
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

    private static LoginViewModel Build(AuthenticationTestBed bed, FakeNavigator navigator) =>
        new(
            new ListActiveUsersUseCase(bed.Users, bed.Units),
            new AuthenticateUserUseCase(bed.Users, bed.Hasher, bed.Session, bed.Units),
            navigator);
}
