using FrubberPOS.Application.Authentication;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Users;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

public sealed class MainViewModelTests
{
    [Fact]
    public void Cashier_SeesOnlySaleAndCash()
    {
        var navigator = new FakeNavigator();
        var viewModel = new MainViewModel(
            new SignedInUser(2, "Zed", UserRole.Cashier),
            new SignOutUseCase(new FakeCurrentSession()),
            navigator);

        var titles = viewModel.MenuItems.Select(m => m.Title).ToList();

        Assert.Equal(["Venta", "Caja"], titles);
        Assert.False(viewModel.IsAdmin);
        Assert.Equal("Venta", viewModel.PageTitle);
    }

    [Fact]
    public void Admin_SeesEverything()
    {
        var navigator = new FakeNavigator();
        var viewModel = new MainViewModel(
            new SignedInUser(1, "Admin", UserRole.Admin),
            new SignOutUseCase(new FakeCurrentSession()),
            navigator);

        var titles = viewModel.MenuItems.Select(m => m.Title).ToList();

        Assert.Equal(["Venta", "Caja", "Productos", "Inventario", "Usuarios", "Historial", "Configuración"], titles);
        Assert.True(viewModel.IsAdmin);
    }

    [Fact]
    public async Task SignOut_ClearsSessionAndShowsLogin()
    {
        var session = new FakeCurrentSession();
        session.SignIn(new SignedInUser(2, "Zed", UserRole.Cashier));
        var navigator = new FakeNavigator();
        var viewModel = new MainViewModel(
            session.Current!,
            new SignOutUseCase(session),
            navigator);

        await viewModel.SignOutAsync();

        Assert.Null(session.Current);
        Assert.Equal(1, navigator.LoginCount);
    }
}
