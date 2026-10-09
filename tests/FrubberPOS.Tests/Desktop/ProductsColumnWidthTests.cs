using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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
            view.Width = 1600;
            view.Height = 900;
            view.Measure(new Size(1600, 900));
            view.Arrange(new Rect(0, 0, 1600, 900));
            view.UpdateLayout();
            StaViewHarness.PumpLoaded(view);
            view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            var grid = Descendants(view).OfType<DataGrid>().Single();
            var report = $"gridW={grid.ActualWidth:F0}";

            var producto = grid.Columns.Single(c => Equals(c.Header, "Producto"));
            report += $" Producto={producto.ActualWidth:F0}";

            var rightAligned = new[] { "Precio", "System.Windows.Controls.TextBlock" };
            var deltas = new List<(string Name, double Delta, bool Right)>();
            for (var i = 0; i < grid.Columns.Count; i++)
            {
                var col = grid.Columns[i];
                var name = col.Header?.ToString() ?? "?";
                var header = HeaderContent(grid, col);
                var cell = CellContent(grid, 0, i);
                var h = Bounds(header, grid);
                var v = Bounds(cell, grid);
                var isRight = rightAligned.Contains(name);
                var delta = isRight ? h.right - v.right : h.left - v.left;
                deltas.Add((name, delta, isRight));
                report += $"\n  [{name}] w={col.ActualWidth:F0} header=[{h.left:F0}..{h.right:F0}] value=[{v.left:F0}..{v.right:F0}] d={(isRight ? "R" : "L")}{delta:F2}";
            }

            Assert.True(producto.ActualWidth >= 300, report);

            // Cabecera y valor comparten banda [col+10..col-10]: desfase máximo 1 px.
            foreach (var d in deltas)
            {
                Assert.True(Math.Abs(d.Delta) <= 1.0, $"desalineado {d.Name}: {d.Delta:F2}\n{report}");
            }

            // Existencia y Estado con separación real (antes: 2 px, "encimados").
            var existenciaValue = Bounds(CellContent(grid, 0, grid.Columns.Count - 2), grid);
            var estadoValue = Bounds(CellContent(grid, 0, grid.Columns.Count - 1), grid);
            var gap = estadoValue.left - existenciaValue.right;
            report += $"\n  gapExistenciaEstado={gap:F1}";
            Assert.True(gap >= 16, report);
        });
    }

    private static (double left, double right) Bounds(FrameworkElement el, FrameworkElement root)
    {
        var p = el.TransformToAncestor(root).Transform(new Point(0, 0));
        return (p.X, p.X + el.ActualWidth);
    }

    private static string Chain(DependencyObject el, FrameworkElement root)
    {
        var parts = new List<string>();
        DependencyObject? current = el;
        while (current is FrameworkElement fe && !ReferenceEquals(current, root))
        {
            var b = Bounds(fe, root);
            parts.Add($"{fe.GetType().Name}[{b.left:F0}..{b.right:F0}] ha={fe.HorizontalAlignment} pad={(fe is Control c ? c.Padding.ToString() : "-")} mar={fe.Margin}");
            current = VisualTreeHelper.GetParent(fe);
        }

        return string.Join(" < ", parts);
    }

    private static FrameworkElement HeaderContent(FrameworkElement root, DataGridColumn col)
    {
        var header = Descendants(root).OfType<DataGridColumnHeader>().First(h => h.Column == col);
        if (header.Content is FrameworkElement fe)
        {
            return fe;
        }

        var text = header.Content?.ToString() ?? string.Empty;
        return Descendants(header).OfType<TextBlock>().First(t => t.Text == text);
    }

    private static FrameworkElement CellContent(FrameworkElement root, int rowIndex, int columnIndex)
    {
        var row = Descendants(root).OfType<DataGridRow>().ElementAt(rowIndex);
        var cell = Descendants(row).OfType<DataGridCell>().ElementAt(columnIndex);
        return Descendants(cell).OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}
