using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Domain.Users;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Smoke build of the real WPF views with the approved theme loaded, on an
/// STA thread. Catches XAML that compiles but explodes at runtime — exactly
/// the JHO-59 regression that made every login die with
/// "Tabular is not a valid value for FontNumeralStyle" when the TPV opened.
/// Shared harness: <see cref="StaViewHarness"/>.
/// </summary>
public sealed class ViewConstructionTests
{
    private static TpvViewModel BuildTpvViewModel(ProductTestBed bed)
    {
        var navigator = new FakeNavigator { NextQuantityMilli = 1_000 };
        return new TpvViewModel(
            new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock),
            new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession()),
            new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units),
            new FakeTicketPrinter(),
            navigator);
    }

    [Fact]
    public async Task Shell_TpvAndCheckout_ConstructWithoutXamlErrors()
    {
        await using var bed = await ProductTestBed.CreateAsync();

        // Todo dentro del hilo STA: MainViewModel construye su página por
        // defecto (el TPV) en el constructor, y WPF exige STA.
        StaViewHarness.Run(() =>
        {
            var tpvViewModel = BuildTpvViewModel(bed);
            var mainViewModel = new MainViewModel(
                new SignedInUser(1, "Admin", UserRole.Admin),
                new SignOutUseCase(new FakeCurrentSession()),
                new FakeNavigator(),
                tpvPageFactory: () => new TpvView(tpvViewModel));

            _ = new FrubberPOS.Desktop.MainWindow(mainViewModel);
            _ = new TpvView(tpvViewModel);
            _ = new CheckoutDialog(new CheckoutViewModel(), 10_000, 0);
            _ = new SplashWindow();
        });
    }

    [Fact]
    public void CheckoutDialog_EveryInstanceStartsInCash()
    {
        // JHO-67: regresión del chip de pago — la segunda instancia del
        // diálogo arrancaba en Transferencia (el TwoWay del chip derecho
        // escribía al VM en el montaje); todas deben iniciar en Efectivo.
        StaViewHarness.Run(() =>
        {
            for (var instance = 1; instance <= 2; instance++)
            {
                var viewModel = new CheckoutViewModel();
                var dialog = new CheckoutDialog(viewModel, 12_000, 0, itemCount: 2);
                dialog.Show();
                Assert.True(viewModel.IsCash, $"el diálogo {instance} debe arrancar en efectivo");
                dialog.Close();
            }
        });
    }
}
