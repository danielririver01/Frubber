using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// B3 (JHO-67): precedencia de teclado del TPV en hilo STA real —
/// Enter/Esc/↑↓ mandan mientras se escribe en el buscador, Enter en el
/// ticket abre el cobro y los botones ✎/🗑 de cada fila hacen
/// select-then-execute sobre su propia línea.
/// </summary>
public sealed class TpvKeyboardTests
{
    private static (TpvViewModel vm, FakeNavigator nav, FakeTicketPrinter printer) Build(ProductTestBed bed)
    {
        var nav = new FakeNavigator { NextQuantityMilli = 1_000 };
        var printer = new FakeTicketPrinter();
        return (new TpvViewModel(
            new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()),
            new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock),
            new GetOpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession()),
            new GetTicketForSaleUseCase(bed.Sales, bed.Settings, bed.Units),
            printer,
            nav), nav, printer);
    }

    private static async Task SeedAdminUserAsync(ProductTestBed bed)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (1, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { Salt = new byte[16], Hash = new byte[32] });
    }

    private static async Task CreateUnitProductAsync(ProductTestBed bed, string plu, string name)
    {
        await new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession()).ExecuteAsync(
            new CreateProductRequest(plu, name, Domain.Products.ProductUnit.Unit, 4_200, 0),
            CancellationToken.None);
    }

    /// <summary>Bomba de dispatcher: drena la cola WPF y cede tiempo al threadpool.</summary>
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

    private static KeyEventArgs RaiseKey(FrameworkElement element, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        element.RaiseEvent(args);
        return args;
    }

    private static TpvView ShowView(Window window, TpvViewModel vm)
    {
        var view = new TpvView(vm);
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

    private static Window NewWindow() => new()
    {
        Width = 1280,
        Height = 800,
        ShowInTaskbar = false,
        WindowState = WindowState.Normal,
    };

    [Fact]
    public async Task SearchBox_ArrowsMoveHighlight_AndEnterAddsHighlighted()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await CreateUnitProductAsync(bed, "L1", "Leche");
        await CreateUnitProductAsync(bed, "P1", "Pan");
        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            try
            {
                var view = ShowView(window, vm);
                var searchBox = (TextBox)view.FindName("SearchBox");
                PumpUntil(() => searchBox.IsKeyboardFocusWithin, "el buscador toma el foco al cargar");

                RaiseKey(view, Key.Up); // tope superior: sigue en la primera
                Assert.Equal(0, vm.Results.IndexOf(vm.SelectedResult!));

                RaiseKey(view, Key.Down);
                Assert.Equal(1, vm.Results.IndexOf(vm.SelectedResult!)); // ↑↓ mandan (B3)

                var expected = vm.Results[1].Name;
                RaiseKey(view, Key.Enter);

                PumpUntil(() => vm.Lines.Count == 1, "Enter agrega el resaltado");
                Assert.Equal(expected, vm.Lines[0].Name);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public async Task SearchBox_EnterWithoutArrow_FallsBackToFirstRow()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await CreateUnitProductAsync(bed, "L1", "Leche");
        await CreateUnitProductAsync(bed, "P1", "Pan");
        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            try
            {
                var view = ShowView(window, vm);
                var searchBox = (TextBox)view.FindName("SearchBox");
                PumpUntil(() => searchBox.IsKeyboardFocusWithin, "foco en el buscador");

                var expected = vm.Results[0].Name;
                RaiseKey(view, Key.Enter);

                PumpUntil(() => vm.Lines.Count == 1, "Enter agrega la primera fila");
                Assert.Equal(expected, vm.Lines[0].Name);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public async Task SearchBox_SingleResult_EnterAddsIt()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await CreateUnitProductAsync(bed, "L1", "Leche");
        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            try
            {
                var view = ShowView(window, vm);
                var searchBox = (TextBox)view.FindName("SearchBox");
                PumpUntil(() => searchBox.IsKeyboardFocusWithin, "foco en el buscador");
                Assert.Single(vm.Results); // JHO-74: la búsqueda devuelve un único producto

                RaiseKey(view, Key.Enter);

                PumpUntil(() => vm.Lines.Count == 1, "Enter agrega el único resultado");
                Assert.Equal("Leche", vm.Lines[0].Name);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public async Task ResultsGrid_RowFocus_EnterAddsHighlighted()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await CreateUnitProductAsync(bed, "L1", "Leche");
        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            try
            {
                var view = ShowView(window, vm);
                var searchBox = (TextBox)view.FindName("SearchBox");
                PumpUntil(() => searchBox.IsKeyboardFocusWithin, "foco inicial en el buscador");

                // JHO-74: el clic natural en el único resultado mueve el foco a
                // la fila; Enter debe agregar igual (antes quedaba sin efecto).
                var resultsGrid = (DataGrid)view.FindName("ResultsGrid");
                PumpUntil(() => resultsGrid.Items.Count == 1, "un único resultado listo");
                resultsGrid.Focus();
                PumpUntil(() => resultsGrid.IsKeyboardFocusWithin, "el foco queda en la fila de resultados");

                RaiseKey(view, Key.Enter);

                PumpUntil(() => vm.Lines.Count == 1, "Enter agrega con el foco en la fila");
                Assert.Equal("Leche", vm.Lines[0].Name);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public async Task SearchBox_EscapeClearsQuery_AndRestoresFullList()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await CreateUnitProductAsync(bed, "L1", "Leche");
        await CreateUnitProductAsync(bed, "P1", "Pan");
        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            try
            {
                var view = ShowView(window, vm);
                var searchBox = (TextBox)view.FindName("SearchBox");
                PumpUntil(() => searchBox.IsKeyboardFocusWithin, "foco en el buscador");

                searchBox.Text = "leche";
                PumpUntil(() => vm.Results.Count == 1, "la búsqueda filtra a un producto");

                RaiseKey(view, Key.Escape);

                PumpUntil(() => vm.SearchText == string.Empty && vm.Results.Count == 2,
                    "Esc limpia la consulta y restaura el catálogo");
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public async Task SearchBox_DeleteWhileTyping_DoesNotRemoveLine()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await CreateUnitProductAsync(bed, "L1", "Leche");
        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            try
            {
                var view = ShowView(window, vm);
                var searchBox = (TextBox)view.FindName("SearchBox");
                PumpUntil(() => searchBox.IsKeyboardFocusWithin, "foco en el buscador");

                RaiseKey(view, Key.Delete);

                Assert.Single(vm.Lines); // Supr nunca borra mientras se escribe
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public async Task TicketGrid_Enter_OpensCheckout()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await CreateUnitProductAsync(bed, "L1", "Leche");
        await SeedAdminUserAsync(bed);
        await new OpenCashSessionUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock)
            .ExecuteAsync(new OpenCashSessionRequest(0), CancellationToken.None);

        var (vm, nav, _) = Build(bed);
        await vm.RefreshCashStateAsync();
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single();
        await vm.AddSelectedAsync();

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            try
            {
                var view = ShowView(window, vm);
                var searchBox = (TextBox)view.FindName("SearchBox");
                PumpUntil(() => searchBox.IsKeyboardFocusWithin, "OnLoaded terminó (foco inicial en el buscador)");

                var ticketGrid = (DataGrid)view.FindName("TicketGrid");
                PumpUntil(() => vm.IsCashOpen, "la sesión de caja sigue abierta");

                ticketGrid.Focus();
                PumpUntil(() => ticketGrid.IsKeyboardFocusWithin, "el ticket toma el foco");

                RaiseKey(view, Key.Enter);

                PumpUntil(() => nav.ShowCheckoutCount == 1, "Enter en el ticket abre el cobro");
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public async Task RowActions_SelectTheirOwnLine_BeforeExecuting()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await CreateUnitProductAsync(bed, "L1", "Leche");
        await CreateUnitProductAsync(bed, "P1", "Pan");
        var (vm, _, _) = Build(bed);
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results.Single(r => r.Plu == "L1");
        await vm.AddSelectedAsync();
        vm.SelectedResult = vm.Results.Single(r => r.Plu == "P1");
        await vm.AddSelectedAsync();
        Assert.Equal(2, vm.Lines.Count);

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            try
            {
                var view = ShowView(window, vm);
                var searchBox = (TextBox)view.FindName("SearchBox");
                PumpUntil(() => searchBox.IsKeyboardFocusWithin && vm.Lines.Count == 2,
                    "OnLoaded terminó y las dos líneas están en el ticket");

                // La selección recae en la primera; el botón de la SEGUNDA fila
                // debe operar sobre la segunda (select-then-execute, B3/M7).
                vm.SelectedLine = vm.Lines[0];

                var ticketGrid = (DataGrid)view.FindName("TicketGrid");
                var removeButtons = Descendants(ticketGrid)
                    .OfType<Button>()
                    .Where(b => AutomationProperties.GetName(b) == "Quitar línea")
                    .ToList();
                Assert.Equal(2, removeButtons.Count); // una por fila con ✎/🗑
                Assert.True(removeButtons[0].ActualWidth >= 36, "objetivo mínimo 36px (M7)");
                Assert.True(removeButtons[0].ActualHeight >= 36, "objetivo mínimo 36px (M7)");

                var secondRowRemove = removeButtons[1];
                secondRowRemove.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = ButtonBase.ClickEvent,
                });

                PumpUntil(() => vm.Lines.Count == 1, "la fila clickeada se quita");

                Assert.Equal("Leche", vm.Lines[0].Name); // Pan salió, aunque Leche estaba seleccionada
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    /// <summary>
    /// Regresión (post JHO-75): la página del TPV se cachea con el mismo
    /// ViewModel de sesión; al volver a Venta debe refrescar el catálogo
    /// para mostrar productos creados mientras no se miraba.
    /// </summary>
    [Fact]
    public async Task Loaded_Revisit_ShowsProductsCreatedWhileAway()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await CreateUnitProductAsync(bed, "L1", "Leche");
        var (vm, _, _) = Build(bed);

        StaViewHarness.Run(() =>
        {
            var window = NewWindow();
            try
            {
                var view = ShowView(window, vm);
                PumpUntil(() => vm.Results.Count == 1, "OnLoaded carga el catálogo inicial");

                CreateUnitProductAsync(bed, "P1", "Pan").GetAwaiter().GetResult();

                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                PumpUntil(() => vm.Results.Count == 2, "volver a Venta refresca los resultados");
                Assert.Contains(vm.Results, r => r.Name == "Pan");
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }
}
