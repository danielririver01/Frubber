using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Authentication;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// JHO-75: bandas de cabecera/celda alineadas en TPV (resultados y ticket),
/// Historial, Usuarios y Detalle de venta — el mismo fix de JHO-74
/// (CellText + HeaderRight, tolerancia 1 px). Los ViewModels con brushes
/// o colecciones observables se construyen dentro del hilo STA.
/// </summary>
public sealed class TableAlignmentTests
{
    [Fact]
    public async Task TpvResultsAndTicketGrids_AreAligned()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("4011", "Aguacate", ProductUnit.Kg, 12_000, 10_000), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("L1", "Leche x1", ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);

        StaViewHarness.Run(() =>
        {
            var vm = new TpvViewModel(
                new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
                new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock),
                new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession()),
                new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units),
                new FakeTicketPrinter(),
                new FakeNavigator { NextQuantityMilli = 1_000 });
            vm.SearchAsync().GetAwaiter().GetResult();
            vm.SelectedResult = vm.Results.First();
            vm.AddSelectedAsync().GetAwaiter().GetResult();
            // El borde teal de la fila seleccionada (3 px) desplaza las celdas
            // del ticket; medir sin selección evita el falso negativo.
            vm.SelectedResult = null;
            vm.SelectedLine = null;

            var view = new TpvView(vm);
            DataGridAlignmentProbe.Layout(view);

            var results = DataGridAlignmentProbe.Descendants(view)
                .OfType<DataGrid>()
                .Single(g => Equals(g.Name, "ResultsGrid"));
            DataGridAlignmentProbe.AssertAligned(view, results, "TPV Results");

            var ticket = DataGridAlignmentProbe.Descendants(view)
                .OfType<DataGrid>()
                .Single(g => Equals(g.Name, "TicketGrid"));
            DataGridAlignmentProbe.AssertAligned(view, ticket, "TPV Ticket");
        });
    }

    [Fact]
    public async Task SalesHistoryGrid_IsAligned()
    {
        await using var bed = await BedWithSalesAsync(2);

        StaViewHarness.Run(() =>
        {
            var vm = new SalesHistoryViewModel(
                new SearchSalesHistoryUseCase(bed.History, bed.Units, ProductTestBed.AdminSession()),
                new FakeNavigator());
            vm.InitializeAsync().GetAwaiter().GetResult();
            Assert.True(vm.Items.Count > 0);

            var view = new SalesHistoryView(vm);
            DataGridAlignmentProbe.Layout(view);
            var grid = DataGridAlignmentProbe.Descendants(view)
                .OfType<DataGrid>()
                .Single();
            DataGridAlignmentProbe.AssertAligned(view, grid, "SalesHistory");
        });
    }

    [Fact]
    public async Task UsersGrid_IsAligned()
    {
        await using var bed = await AuthenticationTestBed.CreateAsync();

        StaViewHarness.Run(() =>
        {
            var vm = new UsersViewModel(
                new ListUsersUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession()),
                new SetUserActiveStateUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession()),
                new DeleteUserUseCase(bed.Users, bed.Units, AuthenticationTestBed.AdminSession()),
                new FakeNavigator());
            vm.LoadAsync().GetAwaiter().GetResult();
            Assert.True(vm.Users.Count > 0);

            var view = new UsersView(vm);
            DataGridAlignmentProbe.Layout(view);
            var grid = DataGridAlignmentProbe.Descendants(view)
                .OfType<DataGrid>()
                .Single();
            DataGridAlignmentProbe.AssertAligned(view, grid, "Users");
        });
    }

    [Fact]
    public async Task SaleDetailGrid_IsAligned()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await SeedOneSaleAsync(bed);

        StaViewHarness.Run(() =>
        {
            var vm = new SaleDetailViewModel(
                new GetSaleDetailUseCase(bed.History, bed.Units, ProductTestBed.AdminSession()),
                new VoidSaleUseCase(bed.Voids, bed.Sales, bed.Units, ProductTestBed.AdminSession(), bed.Clock),
                new FakeNavigator());
            vm.LoadAsync(1, viewerIsAdmin: true).GetAwaiter().GetResult();
            Assert.True(vm.Lines.Count > 0);

            var dialog = new SaleDetailDialog(vm, 1, viewerIsAdmin: true);
            // Window sin Show no reparte el árbol visual (DataGrid no aparece).
            dialog.ShowInTaskbar = false;
            dialog.Show();
            try
            {
                DataGridAlignmentProbe.Layout(dialog, 560, 900);
                var grid = DataGridAlignmentProbe.Descendants(dialog)
                    .OfType<DataGrid>()
                    .Single();
                DataGridAlignmentProbe.AssertAligned(dialog, grid, "SaleDetail");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static async Task<ProductTestBed> BedWithSalesAsync(int count)
    {
        var bed = await ProductTestBed.CreateAsync();
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (@UserId, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { UserId = 1, Salt = new byte[16], Hash = new byte[32] });
        await connection.ExecuteAsync(
            "INSERT INTO CashSession (CashSessionId, OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1, 1, 0, 1, 'Admin', 0);");

        var local = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Unspecified);
        var baseMs = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUnixTimeMilliseconds();
        for (long number = 1; number <= count; number++)
        {
            await connection.ExecuteAsync(
                "INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,"
                + " SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)"
                + " VALUES (@Number, 1, 1, 'Admin', @Moment, @Offset, 1000, 0, 1000, 'CASH', 1000, 0);",
                new
                {
                    Number = number,
                    Moment = baseMs + number * 1_000,
                    Offset = (int)TimeZoneInfo.Local.GetUtcOffset(local).TotalMinutes
                });
        }

        return bed;
    }

    private static async Task SeedOneSaleAsync(ProductTestBed bed)
    {
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
                + " VALUES (@UserId, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
                new { UserId = 1, Salt = new byte[16], Hash = new byte[32] });
            await connection.ExecuteAsync(
                "INSERT INTO CashSession (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
                + " VALUES (1, 0, 1, 'Admin', 0);");
        }

        var created = await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession())
            .ExecuteAsync(new CreateProductRequest("M1", "Mango", ProductUnit.Kg, 6_000, 5_000), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);

        var finalized = await new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock)
            .ExecuteAsync(
                new FinalizeSaleRequest(
                    [new DraftSaleLine(created.Value.ProductId, "M1", "Mango", ProductUnit.Kg, 6_000, 1_250)],
                    500, "CASH", 8_000),
                CancellationToken.None);
        Assert.True(finalized.IsSuccess, finalized.Error?.Message);
    }
}
