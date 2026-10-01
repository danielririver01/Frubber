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
/// </summary>
public sealed class ViewConstructionTests
{
    private static readonly string ThemePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "FrubberPOS.Desktop", "Themes", "FrubberTheme.xaml"));

    private static readonly object Gate = new();

    private static void EnsureApplicationWithTheme()
    {
        if (System.Windows.Application.Current is not null)
        {
            return;
        }

        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(
            new System.Windows.ResourceDictionary { Source = new Uri(ThemePath, UriKind.Absolute) });
    }

    private static void ConstructOnStaThread(Action construct)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                lock (Gate)
                {
                    EnsureApplicationWithTheme();
                    construct();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Timeout construyendo la vista en el hilo STA.");
        Assert.True(failure is null, $"La vista falló al construirse: {failure}");
    }

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
        ConstructOnStaThread(() =>
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
        });
    }
}
