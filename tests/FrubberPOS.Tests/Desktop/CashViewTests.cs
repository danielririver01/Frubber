using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Tests.Cash;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;
using Npgsql;

using FrubberPOS.Application.Persistence;
using FrubberPOS.Infrastructure.Persistence;
namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// JHO-84: la página de Caja se cachea con el mismo ViewModel de sesión;
/// al volver tras registrar una venta debe refrescar los totales (igual
/// que TpvView refresca en cada load). Sin refresh, la segunda visita
/// muestra el estado viejo: 0 ventas y $ 0.
/// </summary>
public sealed class CashViewTests
{
    private static (CashViewModel vm, FakeNavigator nav) Build(CashTestBed bed)
    {
        var nav = new FakeNavigator();
        var session = CashTestBed.AdminSession();
        var vm = new CashViewModel(
            new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, session),
            new OpenCashSessionUseCase(bed.Sessions, bed.Units, session, bed.Clock, bed.Gate),
            new CloseCashSessionUseCase(bed.Sessions, bed.Units, session, bed.Clock, bed.Gate),
            new CorrectOpeningBalanceUseCase(bed.Sessions, bed.Units, session),
            session,
            nav);
        return (vm, nav);
    }

    [Fact]
    public async Task Loaded_Revisit_ShowsSaleMadeWhileAway()
    {
        await using var bed = await CashTestBed.CreateAsync();
        var (vm, _) = Build(bed);
        await vm.RefreshAsync();
        vm.OpeningBalanceText = "50.000";
        await vm.OpenAsync();

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            try
            {
                var view = ShowView(window, vm);
                PumpUntil(() => vm.SaleCountText == "0 ventas", "OnLoaded carga el estado inicial de la caja");

                StaViewHarness.Await(InsertCashSaleAsync(bed.Units, amountCop: 8_400));

                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                PumpUntil(() => vm.SaleCountText == "1 ventas", "volver a Caja refresca la venta registrada (JHO-84)");
                Assert.Equal("$ 8.400", vm.CashSalesText);
                Assert.Equal("$ 58.400", vm.ExpectedCashText);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    /// <summary>Venta CASH válida pegada al esquema 001 (CHECKs de Sale).</summary>
    private static async Task InsertCashSaleAsync(NpgsqlUnitOfWorkFactory units, long amountCop)
    {
        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, CancellationToken.None);
        var sessionId = await unit.Connection.QuerySingleAsync<int>(
            "SELECT CashSessionId FROM CashSession WHERE ClosedAtUtcMs IS NULL;");
        await unit.Connection.ExecuteAsync(
            "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
            + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop,"
            + " PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (1, @SessionId, 1, 'Admin', 1790000000000, -300, @Amount, 0, @Amount, 'CASH', @Amount, 0);",
            new { SessionId = sessionId, Amount = amountCop },
            unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);
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

    private static CashView ShowView(Window window, CashViewModel vm)
    {
        var view = new CashView(vm);
        window.Content = view;
        window.Show();
        window.Activate();
        return view;
    }

    private static void CloseWindow(Window window)
    {
        window.Close();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static Window NewWindow() => new()
    {
        Width = 1280,
        Height = 800,
        ShowInTaskbar = false,
        WindowState = WindowState.Normal,
    };
}
