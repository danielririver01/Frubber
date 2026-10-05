using System.Windows;
using System.Windows.Threading;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// JHO-64: el modal de licencia debe cubrir exactamente a su owner (el bug
/// original redimensionaba en Loaded sin reposicionar, dejando el modal
/// corrido hacia la derecha fuera de la ventana).
/// </summary>
public sealed class LicenseWindowTests
{
    [Fact]
    public void Show_WithOwner_CoversOwnerExactly()
    {
        StaViewHarness.Run(() =>
        {
            var owner = new Window
            {
                WindowStyle = WindowStyle.None,
                Width = 1280,
                Height = 800,
                Left = 150,
                Top = 120,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            owner.Show();

            var license = new LicenseWindow(BuildViewModel());
            license.Owner = owner;
            try
            {
                license.Show();
                PumpUntilIdle(license);

                Assert.Equal(owner.Left, license.Left);
                Assert.Equal(owner.Top, license.Top);
                Assert.Equal(owner.ActualWidth, license.Width);
                Assert.Equal(owner.ActualHeight, license.Height);
            }
            finally
            {
                license.Close();
                owner.Close();
            }
        });
    }

    /// <summary>Bomba el dispatcher de la ventana hasta que no queden tareas (Loaded incluido).</summary>
    private static void PumpUntilIdle(Window window)
    {
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.Background);
        Dispatcher.PushFrame(frame);
    }

    private static LicenseActivationViewModel BuildViewModel()
    {
        var machine = new FakeMachineCodeProvider("0123456789ABCDEF");
        var repo = new FakeLicenseRepository();
        return new LicenseActivationViewModel(
            new GetMachineCodeUseCase(machine),
            new ActivateLicenseUseCase(
                machine,
                new FakeLicenseSignatureVerifier((_, _, _, _) => true),
                new FakeLicenseProtector(),
                repo,
                new FakeUnitOfWorkFactory(),
                new FakeClock(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero))),
            new GetLicenseStatusUseCase(
                machine,
                new FakeLicenseProtector(),
                repo,
                new FakeUnitOfWorkFactory()));
    }
}
