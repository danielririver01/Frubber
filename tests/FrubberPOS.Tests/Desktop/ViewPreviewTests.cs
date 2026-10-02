using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Trial;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Domain.Users;
using FrubberPOS.Tests.Authentication;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Renders the real views to PNG (offscreen, STA) so each redesign round can
/// be compared with the owner's reference images without a production PIN
/// (validation support for JHO-62 rounds). Output: %TEMP%\opencode\frubber-preview.
/// Also serves as a smoke "renders without exceptions" check.
/// </summary>
public sealed class ViewPreviewTests
{
    [Fact]
    public async Task RenderKeyViews_ForVisualReview()
    {
        await using var authBed = await AuthenticationTestBed.CreateAsync();
        await using var prodBed = await ProductTestBed.CreateAsync();

        // Datos de muestra para que la vista previa del TPV/TPV+shell no salga vacía.
        var create = new CreateProductUseCase(prodBed.Products, prodBed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("4011", "Aguacate", Domain.Products.ProductUnit.Kg, 12_000, 10_000), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("L1", "Leche x1", Domain.Products.ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);

        StaViewHarness.Run(() =>
        {
            // --- Login ---
            var loginViewModel = new LoginViewModel(
                new ListActiveUsersUseCase(authBed.Users, authBed.Units),
                new AuthenticateUserUseCase(authBed.Users, authBed.Hasher, authBed.Session, authBed.Units),
                new EvaluateTrialUseCase(
                    new GetLicenseStatusUseCase(
                        new FakeMachineCodeProvider("0123456789ABCDEF"),
                        new FakeLicenseProtector(),
                        new FakeLicenseRepository(),
                        new FakeUnitOfWorkFactory()),
                    new FakeTrialRepository(),
                    new FakeUnitOfWorkFactory(),
                    new FakeClock(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero))),
                new FakeNavigator());
            loginViewModel.LoadUsersAsync().GetAwaiter().GetResult();
            StaViewHarness.SavePng(new LoginWindow(loginViewModel), 1280, 800, "login.png");

            // --- Splash ---
            StaViewHarness.SavePng(new SplashWindow(), 440, 520, "splash.png");

            // --- TPV + shell ---
            var navigator = new FakeNavigator { NextQuantityMilli = 1_000 };
            var tpv = new TpvViewModel(
                new SearchProductsForSaleUseCase(prodBed.Products, prodBed.Units, ProductTestBed.AdminSession()),
                new FinalizeSaleUseCase(prodBed.Sales, prodBed.Sessions, prodBed.Units, ProductTestBed.AdminSession(), prodBed.Clock),
                new GetOpenCashSessionUseCase(prodBed.Sessions, prodBed.Units, ProductTestBed.AdminSession()),
                new GetTicketForSaleUseCase(prodBed.Sales, prodBed.Settings, prodBed.Units),
                new FakeTicketPrinter(),
                navigator);
            tpv.SearchAsync().GetAwaiter().GetResult();
            tpv.SelectedResult = tpv.Results.First();
            tpv.AddSelectedAsync().GetAwaiter().GetResult();

            var mainViewModel = new MainViewModel(
                new SignedInUser(1, "Jhoan Daniel", UserRole.Admin),
                new SignOutUseCase(new FakeCurrentSession()),
                new FakeNavigator(),
                tpvPageFactory: () => new TpvView(tpv),
                licenseBadgeText: "Licencia vitalicia",
                isLicensed: true,
                draftBadgeSource: tpv);
            StaViewHarness.SavePng(new FrubberPOS.Desktop.MainWindow(mainViewModel), 1600, 900, "shell.png");
            StaViewHarness.SavePng(new TpvView(tpv), 1180, 760, "tpv.png");

            // --- Cobro ---
            var checkout = new CheckoutViewModel();
            StaViewHarness.SavePng(new CheckoutDialog(checkout, 12_000, 0), 760, 560, "checkout.png");
        });

        var saved = Directory.GetFiles(Path.Combine(Path.GetTempPath(), "opencode", "frubber-preview"), "*.png");
        Assert.True(saved.Length >= 4, $"Se esperaban 4 PNG, hay {saved.Length}.");
    }
}
