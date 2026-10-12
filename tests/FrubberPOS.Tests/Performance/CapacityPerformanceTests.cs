using System.Diagnostics;
using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Domain.Products;
using FrubberPOS.Tests.Products;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace FrubberPOS.Tests.Performance;

/// <summary>
/// Capacity and performance targets (JHO-55, RNF-REN-003/004/005/006,
/// RN-OPE-005): 5.000 products and 100.000 representative sales over local
/// storage only. Every query reports its elapsed time so the run log
/// records it against the current hardware; assertions use the SRS limits.
/// The database is created from the real migrations and deleted afterwards.
/// </summary>
public sealed class CapacityPerformanceTests : IAsyncDisposable
{
    private static readonly TimeSpan SearchLimit = TimeSpan.FromSeconds(1);       // RNF-REN-003/004
    private static readonly TimeSpan SaleLimit = TimeSpan.FromSeconds(2);         // RNF-REN-005
    private static readonly TimeSpan HistoryLimit = TimeSpan.FromSeconds(3);      // RNF-REN-006

    private readonly List<string> tempDirs = [];
    private readonly ITestOutputHelper output;

    public CapacityPerformanceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var dir in tempDirs)
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        await Task.CompletedTask;
    }

    private async Task<ProductTestBed> VolumeBedAsync()
    {
        var bed = await ProductTestBed.CreateAsync();
        tempDirs.Add(Path.GetDirectoryName(bed.DbPath)!);
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
            + " VALUES (1, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
            new { Salt = new byte[16], Hash = new byte[32] });
        await connection.ExecuteAsync(
            "INSERT INTO CashSession (CashSessionId, OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
            + " VALUES (1, 1, 0, 1, 'Admin', 10_000);");

        var seed = Stopwatch.StartNew();
        // 5.000 productos: kg / lb / unidad alternados (unidad lleva stock entero).
        await connection.ExecuteAsync(@"
WITH RECURSIVE seq(n) AS (
    SELECT 1
    UNION ALL SELECT n + 1 FROM seq WHERE n < 5000
)
INSERT INTO Product (ProductId, Plu, Name, Unit, PriceCop, StockMilli, IsActive, UnitLocked)
SELECT n,
       printf('P%04d', n),
       'Producto ' || printf('%04d', n),
       CASE n % 3 WHEN 0 THEN 'UNIT' WHEN 1 THEN 'KG' ELSE 'LB' END,
       1000 + n,
       CASE n % 3 WHEN 0 THEN n * 1000 ELSE n END,
       1,
       1
FROM seq;");

        // 100.000 ventas con su línea, 100 anuladas (cada milésima).
        var baseMs = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.FromHours(-5)).ToUnixTimeMilliseconds();
        await connection.ExecuteAsync(@"
WITH RECURSIVE seq(n) AS (
    SELECT 1
    UNION ALL SELECT n + 1 FROM seq WHERE n < 100000
)
INSERT INTO Sale (SaleNumber, CashSessionId, CashierUserId, CashierNameSnapshot,
    SoldAtUtcMs, SoldAtOffsetMinutes, SubtotalCop, DiscountCop, TotalCop, PaymentMethod, ReceivedCop, ChangeCop)
SELECT n, 1, 1, 'Admin', @Base + n, -300, 1000, 0, 1000,
       CASE n % 4 WHEN 0 THEN 'TRANSFER' ELSE 'CASH' END,
       CASE n % 4 WHEN 0 THEN NULL ELSE 1000 END,
       CASE n % 4 WHEN 0 THEN NULL ELSE 0 END
FROM seq;", new { Base = baseMs });
        await connection.ExecuteAsync(@"
WITH RECURSIVE seq(n) AS (
    SELECT 1
    UNION ALL SELECT n + 1 FROM seq WHERE n < 100000
)
INSERT INTO SaleLine (SaleId, LineNumber, ProductId, PluSnapshot, ProductNameSnapshot, UnitSnapshot, UnitPriceCopSnapshot, QuantityMilli, LineTotalCop)
SELECT n, 1, 1, 'P0001', 'Producto 0001', 'KG', 1000, 1000, 1000
FROM seq;");
        await connection.ExecuteAsync(
            "INSERT INTO SaleVoid (SaleId, VoidedAtUtcMs, VoidedAtOffsetMinutes, AdminUserId, AdminNameSnapshot)"
            + " SELECT SaleId, SoldAtUtcMs + 1, -300, 1, 'Admin' FROM Sale WHERE SaleNumber % 1000 = 0;");
        await connection.ExecuteAsync("UPDATE SystemSequence SET LastValue = 100000 WHERE SequenceName = 'SaleNumber';");
        seed.Stop();
        output.WriteLine($"[Perf] Carga de volumen (5.000 productos + 100.000 ventas): {seed.Elapsed}");
        return bed;
    }

    [Fact]
    public async Task ProductSearch_UnderOneSecond_AtFiveThousandProducts()
    {
        await using var bed = await VolumeBedAsync();
        var search = new SearchProductsForSaleUseCase(bed.Products, bed.Units, ProductTestBed.CashierSession());
        ReportHardware();

        // PLU exacto.
        var exact = Stopwatch.StartNew();
        var byPlu = await search.ExecuteAsync(new SearchForSaleRequest("P5000"), CancellationToken.None);
        exact.Stop();
        Assert.True(byPlu.IsSuccess);
        Assert.Equal("P5000", byPlu.Value.Items[0].Plu);
        output.WriteLine($"[Perf] TPV búsqueda PLU exacto: {exact.Elapsed} (límite {SearchLimit})");
        Assert.True(exact.Elapsed < SearchLimit, $"Búsqueda por PLU tardó {exact.Elapsed}.");

        // Nombre con coincidencia parcial tardía (recorre el catálogo).
        var byNameWatch = Stopwatch.StartNew();
        var byName = await search.ExecuteAsync(new SearchForSaleRequest("Producto 4999"), CancellationToken.None);
        byNameWatch.Stop();
        Assert.True(byName.IsSuccess);
        Assert.NotEmpty(byName.Value.Items);
        output.WriteLine($"[Perf] TPV búsqueda por nombre: {byNameWatch.Elapsed} (límite {SearchLimit})");
        Assert.True(byNameWatch.Elapsed < SearchLimit, $"Búsqueda por nombre tardó {byNameWatch.Elapsed}.");
    }

    [Fact]
    public async Task AdminCatalogSearch_UnderOneSecond_AtFiveThousandProducts()
    {
        await using var bed = await VolumeBedAsync();
        var search = new SearchProductsUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        ReportHardware();

        var watch = Stopwatch.StartNew();
        var result = await search.ExecuteAsync(new SearchProductsRequest("Producto 499", ProductStatusFilter.Todos), CancellationToken.None);
        watch.Stop();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Products);
        output.WriteLine($"[Perf] Búsqueda administrativa con filtro: {watch.Elapsed} (límite {SearchLimit})");
        Assert.True(watch.Elapsed < SearchLimit, $"Búsqueda de catálogo tardó {watch.Elapsed}.");
    }

    [Fact]
    public async Task HistoryPage_UnderThreeSeconds_AtHundredThousandSales()
    {
        await using var bed = await VolumeBedAsync();
        var history = new SearchSalesHistoryUseCase(bed.History, bed.Units, ProductTestBed.AdminSession());
        ReportHardware();

        var watch = Stopwatch.StartNew();
        var page = await history.ExecuteAsync(
            new SearchSalesHistoryRequest(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 1), CancellationToken.None);
        watch.Stop();

        Assert.True(page.IsSuccess, page.Error?.Message);
        Assert.Equal(100_000, page.Value.TotalCount);
        Assert.Equal(50, page.Value.Items.Count);
        output.WriteLine($"[Perf] Historial página 1 de 100.000 ventas: {watch.Elapsed} (límite {HistoryLimit})");
        Assert.True(watch.Elapsed < HistoryLimit, $"Historial tardó {watch.Elapsed}.");
    }

    [Fact]
    public async Task FinalizeSale_UnderTwoSeconds_AtFullVolume()
    {
        await using var bed = await VolumeBedAsync();
        ReportHardware();

        var finalize = new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate);
        var watch = Stopwatch.StartNew();
        var result = await finalize.ExecuteAsync(
            new FinalizeSaleRequest(
                [new DraftSaleLine(1, "P0001", "Producto 0001", ProductUnit.Kg, 1_001, 500)],
                0, "CASH", 1_000),
            CancellationToken.None);
        watch.Stop();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(100_001, result.Value.SaleNumber); // el consecutivo continuó el volumen
        output.WriteLine($"[Perf] Registro de venta con volumen cargado: {watch.Elapsed} (límite {SaleLimit})");
        Assert.True(watch.Elapsed < SaleLimit, $"La venta tardó {watch.Elapsed}.");
    }

    [Fact]
    public async Task Login_UnderTwoSeconds_WithProductionHashing()
    {
        await using var bed = await FrubberPOS.Tests.Authentication.AuthenticationTestBed.CreateAsync();
        ReportHardware();

        // RNF-REN-002: el inicio de sesión (PBKDF2 210.000 iteraciones reales) no supera 2 s.
        var login = new FrubberPOS.Application.Authentication.AuthenticateUserUseCase(
            bed.Users, bed.Hasher, new FrubberPOS.Tests.TestDoubles.FakeCurrentSession(), bed.Units);
        var watch = Stopwatch.StartNew();
        var result = await login.ExecuteAsync(
            new FrubberPOS.Application.Authentication.AuthenticateUserRequest(1, "1111"), CancellationToken.None);
        watch.Stop();

        Assert.True(result.IsSuccess, result.Error?.Message);
        output.WriteLine($"[Perf] Inicio de sesión con PBKDF2 real: {watch.Elapsed} (límite 2 s)");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"El login tardó {watch.Elapsed}.");
    }

    [Fact]
    public async Task OpenCashSummary_StaysUsable_AtFullVolume()
    {
        await using var bed = await VolumeBedAsync();
        var summary = new GetCashSessionSummaryUseCase(bed.Sessions, bed.Units, ProductTestBed.AdminSession());
        ReportHardware();

        var watch = Stopwatch.StartNew();
        var result = await summary.ExecuteAsync(new GetCashSessionSummaryRequest(1), CancellationToken.None);
        watch.Stop();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(99_900, result.Value.Summary.SaleCount); // 100 anuladas no cuentan
        output.WriteLine($"[Perf] Resumen de caja abierta con 100.000 ventas: {watch.Elapsed}");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Resumen tardó {watch.Elapsed}.");
    }

    private void ReportHardware()
    {
        output.WriteLine(
            $"[Perf] Equipo: {Environment.ProcessorCount} núcleos, "
            + $"{Environment.OSVersion}, .NET {Environment.Version}, "
            + $"memoria del proceso {Environment.WorkingSet / 1_048_576} MB.");
    }
}
