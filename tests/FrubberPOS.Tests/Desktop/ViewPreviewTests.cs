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
            // --- Login: fase de usuario (lg-1) ---
            var loginViewModel = BuildLoginViewModel(authBed);
            loginViewModel.LoadUsersAsync().GetAwaiter().GetResult();
            StaViewHarness.SavePng(new LoginWindow(loginViewModel), 1280, 800, "login.png");

            // --- Login: fase PIN con dos dígitos (lg-3) ---
            var pinViewModel = BuildLoginViewModel(authBed);
            pinViewModel.LoadUsersAsync().GetAwaiter().GetResult();
            pinViewModel.GoToPin();
            pinViewModel.EnterDigit("2");
            pinViewModel.EnterDigit("4");
            StaViewHarness.SavePng(new LoginWindow(pinViewModel), 1280, 800, "login-pin.png");

            // --- Login: modo bloqueo de sesión (lo-*) ---
            var lockViewModel = BuildLoginViewModel(authBed);
            lockViewModel.LoadUsersAsync().GetAwaiter().GetResult();
            lockViewModel.ActivateLockMode();
            StaViewHarness.SavePng(new LoginWindow(lockViewModel), 1280, 800, "login-lock.png");


            // --- Licencia (modal compacto lic-*) ---
            var licenseMachine = new FakeMachineCodeProvider("0123456789ABCDEF");
            var licenseRepo = new FakeLicenseRepository();
            var licenseViewModel = new LicenseActivationViewModel(
                new GetMachineCodeUseCase(licenseMachine),
                new ActivateLicenseUseCase(
                    licenseMachine,
                    new FakeLicenseSignatureVerifier((_, _, _, _) => true),
                    new FakeLicenseProtector(),
                    licenseRepo,
                    new FakeUnitOfWorkFactory(),
                    new FakeClock(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero))),
                new GetLicenseStatusUseCase(
                    licenseMachine,
                    new FakeLicenseProtector(),
                    licenseRepo,
                    new FakeUnitOfWorkFactory()));
            licenseViewModel.LoadAsync().GetAwaiter().GetResult();
            StaViewHarness.SavePng(new LicenseWindow(licenseViewModel), 560, 640, "license.png");

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

            // --- Shell con overlay de bloqueo (JHO-64, lo-*) ---
            var shellLockViewModel = BuildLoginViewModel(authBed);
            shellLockViewModel.LoadUsersAsync().GetAwaiter().GetResult();
            var shellWindow = new FrubberPOS.Desktop.MainWindow(mainViewModel);
            shellWindow.ShowLock(shellLockViewModel);
            StaViewHarness.SavePng(shellWindow, 1600, 900, "shell-lock.png");

            // --- Shell: píldora de aviso de inactividad (20 s antes del lock) ---
            mainViewModel.LastActivityUtc = DateTime.UtcNow - TimeSpan.FromSeconds(160);
            mainViewModel.OnIdleTick();
            StaViewHarness.SavePng(new FrubberPOS.Desktop.MainWindow(mainViewModel), 1600, 900, "shell-idle.png");

            // --- Cobro ---
            var checkout = new CheckoutViewModel();
            StaViewHarness.SavePng(new CheckoutDialog(checkout, 12_000, 0), 760, 560, "checkout.png");
        });

        var saved = Directory.GetFiles(Path.Combine(Path.GetTempPath(), "opencode", "frubber-preview"), "*.png");
        Assert.True(saved.Length >= 6, $"Se esperaban 6 PNG, hay {saved.Length}.");
    }

    private static LoginViewModel BuildLoginViewModel(AuthenticationTestBed bed) =>
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
            new FakeNavigator());
}
