using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Trial;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Tests.Authentication;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// JHO-85: al abrir la tarjeta de login con varios usuarios no hay ninguna
/// fila marcada, y el teclado (↓ sobre la lista con foco) sigue pudiendo
/// elegir la primera fila — el flujo de flechas no depende de la
/// preselección que se quitó.
/// </summary>
public sealed class LoginCardViewTests
{
    [Fact]
    public async Task OnOpen_NoUserChecked_DownArrowSelectsFirstRow()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();
        var viewModel = Build(bed);
        await viewModel.LoadUsersAsync();

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            var view = new LoginCardView { DataContext = viewModel };
            window.Content = view;
            window.Show();
            window.Activate();

            var list = (ListBox)view.FindName("UsersList")!;
            Assert.Equal(3, list.Items.Count);
            Assert.Null(viewModel.SelectedUser);
            Assert.Null(list.SelectedItem);
            Assert.False(viewModel.CanContinue);

            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list), 0, Key.Down)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            list.RaiseEvent(args);

            Assert.True(args.Handled, "la tarjeta debe interceptar la flecha ↓");
            PumpUntil(() => viewModel.SelectedUser is not null, "la flecha ↓ debe marcar la primera fila");
            Assert.Equal(viewModel.Users[0].UserId, viewModel.SelectedUser!.UserId);
            Assert.Equal(viewModel.Users[0].UserId, list.SelectedItem is ActiveUserDto row ? row.UserId : -1);
            Assert.True(viewModel.CanContinue);

            window.Close();
        });
    }

    private static void PumpUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(25);
        }

        Assert.True(condition(), $"Condición no alcanzada: {because}");
    }

    private static Window NewWindow() => new()
    {
        Width = 1280,
        Height = 800,
        ShowInTaskbar = false,
        WindowState = WindowState.Normal,
    };

    private static LoginViewModel Build(AuthenticationTestBed bed) =>
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
