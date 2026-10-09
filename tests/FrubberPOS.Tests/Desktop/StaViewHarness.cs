using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Arnés STA compartido para construir/renderizar vistas WPF en pruebas:
/// crea una única Application con el tema aprobado y ofrece helpers para
/// ejecutar código en un hilo STA y volcar un Visual a PNG.
/// Usado por ViewConstructionTests (regresión de XAML) y ViewPreviewTests
/// (capturas para revisión visual contra los PNG del dueño, JHO-69).
/// </summary>
internal static class StaViewHarness
{
    public static readonly string ThemePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "FrubberPOS.Desktop", "Themes", "FrubberTheme.xaml"));

    private static readonly object Gate = new();

    public static void EnsureApplicationWithTheme()
    {
        if (System.Windows.Application.Current is not null)
        {
            return;
        }

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri(ThemePath, UriKind.Absolute) });
    }

    /// <summary>Ejecuta <paramref name="action"/> en un hilo STA dedicado y repropaga fallos.</summary>
    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                lock (Gate)
                {
                    EnsureApplicationWithTheme();
                    action();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Timeout en hilo STA.");
        Assert.True(failure is null, $"Fallo en hilo STA: {failure}");
    }

    /// <summary>
    /// Devuelve concatenados los <see cref="System.Windows.Controls.TextBlock.Text"/>
    /// de todo el árbol visual ya maquetado (verificación de bindings de texto
    /// sin depender de la lectura del PNG).
    /// </summary>
    public static string CaptureText(FrameworkElement root)
    {
        var texts = new List<string>();
        Walk(root);
        return string.Join("\n", texts);

        void Walk(DependencyObject node)
        {
            if (node is System.Windows.Controls.TextBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))
            {
                texts.Add(textBlock.Text);
            }

            var childCount = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < childCount; i++)
            {
                Walk(VisualTreeHelper.GetChild(node, i));
            }
        }
    }

    /// <summary>Renderiza un visual a PNG; si es una ventana, la muestra un instante para pintar el fondo.</summary>
    public static string SavePng(FrameworkElement element, int width, int height, string fileName, Action<FrameworkElement>? beforeRender = null)
    {
        FrameworkElement target = element;
        FrameworkElement renderTarget = target;
        System.Windows.Window? window = element as System.Windows.Window;
        if (window is not null)
        {
            window.WindowState = WindowState.Normal;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.ShowInTaskbar = false;
            window.Width = width;
            window.Height = height;
            window.Show();
        }
        else
        {
            // Las vistas sueltas no pintan fondo (el shell lo aporta en vivo);
            // sin este host el PNG sale con banda negra y texto invisible.
            var host = new System.Windows.Controls.Border
            {
                Width = width,
                Height = height,
                Background = System.Windows.Application.Current?.TryFindResource("AppBackground") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA)),
                Child = target,
            };
            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            renderTarget = host;
        }

        target.UpdateLayout();
        window?.UpdateLayout();
        PumpLoaded(target);
        target.InvalidateVisual();
        target.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        var renderWidth = (int)Math.Ceiling(renderTarget.ActualWidth > 0 ? renderTarget.ActualWidth : width);
        var renderHeight = (int)Math.Ceiling(renderTarget.ActualHeight > 0 ? renderTarget.ActualHeight : height);
        beforeRender?.Invoke(target);
        var bitmap = new RenderTargetBitmap(renderWidth, renderHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(renderTarget);
        window?.Close();

        var dir = Path.Combine(Path.GetTempPath(), "opencode", "frubber-preview");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return path;
    }

    /// <summary>
    /// Bombea el dispatcher hasta DispatcherPriority.Loaded. El DataGrid de WPF reparte
    /// sus columnas estrella dentro de una BeginInvoke(Loaded) encadenada desde
    /// OnViewportSizeChanged (DataGrid.cs -> RedistributeColumnWidthsOnAvailableSpaceChange);
    /// sin este bombeo las estrellas quedan clavadas en MinColumnWidth (20 px) y cualquier
    /// preview o prueba de medición pura muestra la tabla colapsada (falso negativo, JHO-74).
    /// </summary>
    public static void PumpLoaded(FrameworkElement target)
    {
        var frame = new DispatcherFrame();
        target.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
