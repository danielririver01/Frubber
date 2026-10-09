using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// JHO-74/75: mide la banda de cabecera vs celda de cada columna de un
/// DataGrid real (STA, tras Measure/Arrange + PumpLoaded). Columnas con
/// cabecera o celda alineadas a la derecha comparan bordes derechos; el
/// resto, bordes izquierdos. Tolerancia: 1 px (JHO-74).
/// Debe invocarse dentro de un <see cref="StaViewHarness.Run"/> ya maquetado.
/// </summary>
internal static class DataGridAlignmentProbe
{
    public const double TolerancePx = 1.0;

    /// <summary>Measure/Arrange/PumpLoaded sobre la vista; dentro del mismo STA.</summary>
    public static void Layout(FrameworkElement view, double width = 1600, double height = 900)
    {
        view.Width = width;
        view.Height = height;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        StaViewHarness.PumpLoaded(view);
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    public static void AssertAligned(FrameworkElement root, DataGrid grid, string label)
    {
        var report = $"{label} gridW={grid.ActualWidth:F0}";
        var rows = Descendants(grid).OfType<DataGridRow>().ToList();
        Assert.True(rows.Count > 0, $"{label}: sin filas que medir\n{report}");

        // La fila seleccionada del TPV suma un borde teal de 3 px a la izquierda
        // (TpvGridRow): medir una no seleccionada evita falso negativo (JHO-75).
        var row = rows.FirstOrDefault(r => !r.IsSelected) ?? rows[0];
        var cells = Descendants(row).OfType<DataGridCell>().ToList();

        for (var i = 0; i < grid.Columns.Count; i++)
        {
            var col = grid.Columns[i];
            var name = ColumnName(col);

            // Columnas de acciones sin Header (p. ej. editar/quitar del ticket):
            // no hay banda de cabecera que comparar.
            if (string.IsNullOrWhiteSpace(name) || name == "?")
            {
                report += $"\n  [sin-cabecera] w={col.ActualWidth:F0} (omitida)";
                continue;
            }

            var header = Descendants(root).OfType<DataGridColumnHeader>().First(h => h.Column == col);
            var headerEl = HeaderContent(header);
            var (hLeft, hRight) = Bounds(headerEl, grid);
            var (vLeft, vRight) = CellBand(cells[i], grid);
            var isRight = headerEl.HorizontalAlignment == HorizontalAlignment.Right
                || header.Content is FrameworkElement { HorizontalAlignment: HorizontalAlignment.Right };
            var delta = isRight ? hRight - vRight : hLeft - vLeft;
            report += $"\n  [{name}] w={col.ActualWidth:F0} header=[{hLeft:F0}..{hRight:F0}] value=[{vLeft:F0}..{vRight:F0}] d={(isRight ? "R" : "L")}{delta:F2}";
            Assert.True(
                Math.Abs(delta) <= TolerancePx,
                $"{label}: columna '{name}' desalineada ({delta:F2}px)\n{report}");
        }
    }

    public static (double left, double right) Bounds(FrameworkElement el, FrameworkElement root)
    {
        var p = el.TransformToAncestor(root).Transform(new Point(0, 0));
        return (p.X, p.X + el.ActualWidth);
    }

    public static FrameworkElement HeaderContent(DataGridColumnHeader header)
    {
        if (header.Content is FrameworkElement fe)
        {
            return fe;
        }

        // String header: WPF puede no materializar un TextBlock con Text exacto
        // (p. ej. "#"); el ContentPresenter estira y su banda es la del header.
        var text = header.Content?.ToString() ?? string.Empty;
        var exact = Descendants(header).OfType<TextBlock>().FirstOrDefault(t => t.Text == text);
        if (exact is not null)
        {
            return exact;
        }

        var presenter = Descendants(header).OfType<ContentPresenter>().FirstOrDefault();
        return presenter is not null ? presenter : header;
    }

    /// <summary>
    /// Banda visual de la celda: si el template es un panel full-width
    /// (DockPanel del chip TPV), la banda la fijan los hijos con Margin
    /// (chip a la izquierda, StackPanel/border a la derecha), no el panel.
    /// </summary>
    public static (double left, double right) CellBand(DataGridCell cell, FrameworkElement root)
    {
        var elements = Descendants(cell)
            .OfType<FrameworkElement>()
            .Where(e => e.ActualWidth > 0 && e.Visibility == Visibility.Visible)
            .ToList();

        var withLeftMargin = elements.Where(e => e.Margin.Left > 0).ToList();
        var left = withLeftMargin.Count > 0
            ? withLeftMargin.Min(e => Bounds(e, root).left)
            : elements.OfType<TextBlock>().Where(t => t.Text.Length > 0)
                .Select(e => Bounds(e, root).left)
                .DefaultIfEmpty(Bounds(cell, root).left)
                .Min();

        var withRightBand = elements
            .Where(e => e.Margin.Right > 0 || e.HorizontalAlignment == HorizontalAlignment.Right)
            .ToList();
        var right = withRightBand.Count > 0
            ? withRightBand.Max(e => Bounds(e, root).right)
            : elements.OfType<TextBlock>().Where(t => t.Text.Length > 0)
                .Select(e => Bounds(e, root).right)
                .DefaultIfEmpty(Bounds(cell, root).right)
                .Max();

        return (left, right);
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
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

    private static string ColumnName(DataGridColumn col) =>
        col.Header switch
        {
            TextBlock tb => tb.Text,
            FrameworkElement fe => fe.GetVisualChildOfType<TextBlock>()?.Text ?? "?",
            null => "?",
            var h => h.ToString() ?? "?",
        };

    private static T? GetVisualChildOfType<T>(this DependencyObject el)
        where T : DependencyObject
    {
        foreach (var d in Descendants(el).OfType<T>())
        {
            return d;
        }

        return null;
    }
}
