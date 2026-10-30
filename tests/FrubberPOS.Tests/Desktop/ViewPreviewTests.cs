using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Tests.Cash;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;
using Npgsql;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Previsualizaciones de vista (sin aserciones de datos): solo se construyen
/// y se miden los árboles Visual para confirmar que no hay colapso de
/// layout ni excepciones de XAML con datos reales de PostgreSQL.
/// </summary>
public sealed class ViewPreviewTests
{
    [Fact]
    public async Task CashPage_RendersWithSaleOnIt()
    {
        await using var bed = await CashTestBed.CreateAsync();
        await InsertSaleAsync(bed);

        StaViewHarness.Run(() =>
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
            var view = new CashView(vm);
            DataGridAlignmentProbe.Layout(view);
        });
    }

    private static async Task InsertSaleAsync(CashTestBed bed)
    {
        // JHO-113: CashTestBed solo siembra usuarios; sin caja abierta esta
        // consulta no encontraba ninguna fila y la prueba moría con
        // "Sequence contains no elements".
        await using var unit = await bed.Units.CreateAsync(UnitOfWorkMode.WriteImmediate, CancellationToken.None);
        await unit.Connection.ExecuteAsync(
            "INSERT INTO CashSession (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1790000000000, -300, 1, 'Admin', 0);",
            transaction: unit.Transaction);
        var sessionId = await unit.Connection.QuerySingleAsync<int>(
            "SELECT CashSessionId FROM CashSession WHERE ClosedAtUtcMs IS NULL;",
            transaction: unit.Transaction);
        await unit.Connection.ExecuteAsync(
            "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
            + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop,"
            + " PaymentMethod, ReceivedCop, ChangeCop)"
            + " VALUES (1, @SessionId, 1, 'Admin', 1790000000000, -300, 8400, 0, 8400, 'CASH', 8400, 0);",
            new { SessionId = sessionId },
            unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);
    }
}
