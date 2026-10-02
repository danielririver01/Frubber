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
    public void LicensedAdmin_MenuHidesLicenseEntry()
    {
        var navigator = new FakeNavigator();
        var viewModel = new MainViewModel(
            new SignedInUser(1, "Admin", UserRole.Admin),
            new SignOutUseCase(new FakeCurrentSession()),
            navigator,
            licenseBadgeText: "Licencia vitalicia",
            isLicensed: true);

        Assert.DoesNotContain(viewModel.MenuItems, m => m.Title == "Licencia");
        Assert.Equal("Licencia vitalicia", viewModel.LicenseBadgeText);
        Assert.True(viewModel.LicenseBadgeVisible);
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

    [Fact]
    public async Task VentaBadge_ReflejaLineasDelBorrador()
    {
        // JHO-69: la píldora del menú Venta muestra las líneas en preparación.
        await using var bed = await FrubberPOS.Tests.Products.ProductTestBed.CreateAsync();
        var nav = new FakeNavigator { NextQuantityMilli = 1_000 };
        var tpv = new TpvViewModel(
            new FrubberPOS.Application.Sales.SearchProductsForSaleUseCase(bed.Products, bed.Units, FrubberPOS.Tests.Products.ProductTestBed.AdminSession()),
            new FrubberPOS.Application.Sales.FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, FrubberPOS.Tests.Products.ProductTestBed.AdminSession(), bed.Clock),
            new FrubberPOS.Application.Cash.GetOpenCashSessionUseCase(bed.Sessions, bed.Units, FrubberPOS.Tests.Products.ProductTestBed.AdminSession()),
            new FrubberPOS.Application.Sales.GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units),
            new FakeTicketPrinter(),
            nav);
        var viewModel = new MainViewModel(
            new SignedInUser(1, "Admin", UserRole.Admin),
            new SignOutUseCase(new FakeCurrentSession()),
            new FakeNavigator(),
            tpvPageFactory: () => new object(),
            draftBadgeSource: tpv);

        var venta = viewModel.MenuItems.Single(m => m.Title == "Venta");
        Assert.Equal(0, venta.BadgeCount);
        Assert.False(venta.BadgeVisible);

        await new FrubberPOS.Application.Products.CreateProductUseCase(bed.Products, bed.Units, FrubberPOS.Tests.Products.ProductTestBed.AdminSession())
            .ExecuteAsync(new FrubberPOS.Application.Products.CreateProductRequest("L1", "Leche", Domain.Products.ProductUnit.Unit, 4_200, 0), CancellationToken.None);
        await tpv.SearchAsync();
        tpv.SelectedResult = tpv.Results.Single();
        await tpv.AddSelectedAsync();

        Assert.Equal(1, venta.BadgeCount);
        Assert.True(venta.BadgeVisible);
    }
}
