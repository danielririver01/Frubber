using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Desktop;

public sealed class ProductsColumnWidthTests
{
    [Fact]
    public async Task ProductsGrid_ColumnsAlignedAndStarFills()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        await create.ExecuteAsync(new CreateProductRequest("4011", "Aguacate", Domain.Products.ProductUnit.Kg, 12_000, 10_000), CancellationToken.None);
        await create.ExecuteAsync(new CreateProductRequest("L1", "Leche x1", Domain.Products.ProductUnit.Unit, 4_200, 24_000), CancellationToken.None);

        var vm = new ProductsViewModel(
            new SearchProductsUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new SetProductActiveStateUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new DeleteProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new FakeNavigator());
        await vm.SearchAsync();

        StaViewHarness.Run(() =>
        {
            var view = new ProductsView(vm);
            DataGridAlignmentProbe.Layout(view);
            var grid = DataGridAlignmentProbe.Descendants(view).OfType<DataGrid>().Single();
            DataGridAlignmentProbe.AssertAligned(view, grid, "Products");

            var report = $"gridW={grid.ActualWidth:F0}";
            var producto = grid.Columns.Single(c => Equals(c.Header, "Producto"));
            report += $" Producto={producto.ActualWidth:F0}";
            Assert.True(producto.ActualWidth >= 300, report);

            // Existencia y Estado con separación real (antes: 2 px, "encimados").
            // Los headers de Precio/Existencia son TextBlock, no string.
            var existenciaCol = grid.Columns.First(c => c.Header is TextBlock { Text: "Existencia" });
            var estadoCol = grid.Columns.First(c => Equals(c.Header, "Estado"));
            var cells = DataGridAlignmentProbe.Descendants(grid)
                .OfType<DataGridCell>()
                .ToList();
            var existenciaValue = DataGridAlignmentProbe.CellBand(
                cells.First(c => c.Column == existenciaCol), grid);
            var estadoValue = DataGridAlignmentProbe.CellBand(
                cells.First(c => c.Column == estadoCol), grid);
            var gap = estadoValue.left - existenciaValue.right;
            report += $"\n  gapExistenciaEstado={gap:F1}";
            Assert.True(gap >= 16, report);
        });
    }
}
