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

        Assert.Equal(["Venta", "Caja", "Productos", "Historial", "Usuarios", "Configuración", "Respaldo", "Licencia"], titles);
        Assert.True(viewModel.IsAdmin);
    }

    [Fact]
    public void SelectingLicense_OpensLicenseDialog()
    {
        var navigator = new FakeNavigator();
        var viewModel = new MainViewModel(
            new SignedInUser(1, "Admin", UserRole.Admin),
            new SignOutUseCase(new FakeCurrentSession()),
            navigator);

        viewModel.SelectedEntry = viewModel.MenuItems.Single(m => m.Title == "Licencia");

        Assert.Equal(1, navigator.LicenseCount);
        Assert.Equal("Licencia", viewModel.PageTitle);
    }

    [Fact]
    public void SwitchingPages_ReplacesContentAndCachesEachPageOnce()
    {
        var navigator = new FakeNavigator();
        var productsPage = new object();
        var cashPage = new object();
        var tpvPage = new object();
        var viewModel = new MainViewModel(
            new SignedInUser(1, "Admin", UserRole.Admin),
            new SignOutUseCase(new FakeCurrentSession()),
            navigator,
            () => productsPage,
            () => cashPage,
            () => tpvPage);

        viewModel.SelectedEntry = viewModel.MenuItems.Single(m => m.Title == "Caja");
        Assert.Same(cashPage, viewModel.CurrentPage);

        // Bug JHO-59: Productos no podía reutilizar CurrentPage cacheado.
        viewModel.SelectedEntry = viewModel.MenuItems.Single(m => m.Title == "Productos");
        Assert.Same(productsPage, viewModel.CurrentPage);

        viewModel.SelectedEntry = viewModel.MenuItems.Single(m => m.Title == "Venta");
        Assert.Same(tpvPage, viewModel.CurrentPage);

        // Y al volver, cada página conserva su instancia cacheada.
        viewModel.SelectedEntry = viewModel.MenuItems.Single(m => m.Title == "Caja");
        Assert.Same(cashPage, viewModel.CurrentPage);
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
