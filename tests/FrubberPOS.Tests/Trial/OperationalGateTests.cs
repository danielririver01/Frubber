using System.Security.Cryptography;
using System.Text;
using Dapper;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Trial;
using FrubberPOS.Domain.Licensing;
using FrubberPOS.Domain.Products;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Persistence.Licensing;
using FrubberPOS.Infrastructure.Persistence.Trial;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Trial;

/// <summary>
/// The real operational gate (JHO-57, P-DD §11.3, RN-LIC-002/003/008/010):
/// the licensing decision is produced by <see cref="EvaluateTrialUseCase"/>
/// over the real trial/license tables and re-checked by the transactional
/// use cases — a blocked state refuses the sale without touching data.
/// </summary>
public sealed class OperationalGateTests
{
    private const string KeyId = "lic-test";
    private const string Machine = "0123456789ABCDEF";
    private const long Day = 86_400_000L;

    private sealed record Wiring(OperationalGate Gate, FakeClock Clock, GetLicenseStatusUseCase Status);

    private static Wiring CreateWiring(ProductTestBed bed, FakeClock clock)
    {
        var status = new GetLicenseStatusUseCase(
            new FakeMachineCodeProvider(Machine), new FakeLicenseProtector(), new DapperLicenseRepository(), bed.Units);
        var evaluate = new EvaluateTrialUseCase(status, new DapperTrialRepository(), bed.Units, clock);
        return new Wiring(new OperationalGate(evaluate), clock, status);
    }

    private static async Task SeedTrialAsync(ProductTestBed bed, long firstRunUtcMs, long lastSeenUtcMs)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO TrialState (TrialStateId, FirstRunUtcMs, LastSeenUtcMs) VALUES (1, @First, @Last);",
            new { First = firstRunUtcMs, Last = lastSeenUtcMs });
    }

    [Fact]
    public async Task Gate_FreshInstall_AllowsTheOperation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var wiring = CreateWiring(bed, bed.Clock);

        var blocked = await wiring.Gate.CheckAsync(CancellationToken.None);

        Assert.Null(blocked); // prueba activa: 30 días desde la instalación (RN-LIC-001)
    }

    [Fact]
    public async Task Gate_ExpiredTrial_ReturnsLicenseBlocked()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var nowMs = bed.Clock.UtcNow.ToUnixTimeMilliseconds();
        await SeedTrialAsync(bed, nowMs - 40 * Day, nowMs - 10 * Day); // instalado hace 40 días
        var wiring = CreateWiring(bed, bed.Clock);

        var blocked = await wiring.Gate.CheckAsync(CancellationToken.None);

        Assert.NotNull(blocked);
        Assert.Equal(ErrorCode.LicenseBlocked, blocked.Code);
        Assert.Contains("periodo de prueba", blocked.Message);
    }

    [Fact]
    public async Task Gate_ClockTampered_ReturnsLicenseBlocked()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var nowMs = bed.Clock.UtcNow.ToUnixTimeMilliseconds();
        await SeedTrialAsync(bed, nowMs - 2 * Day, nowMs + Day); // último uso "en el futuro"
        var wiring = CreateWiring(bed, bed.Clock);

        var blocked = await wiring.Gate.CheckAsync(CancellationToken.None);

        Assert.NotNull(blocked);
        Assert.Equal(ErrorCode.LicenseBlocked, blocked.Code);
        Assert.Contains("reloj", blocked.Message);
    }

    [Fact]
    public async Task Gate_LicensedInstallation_AllowsEvenAfterExpiry()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var nowMs = bed.Clock.UtcNow.ToUnixTimeMilliseconds();
        await SeedTrialAsync(bed, nowMs - 40 * Day, nowMs - 10 * Day);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var canonical = LicenseKeyFormat.BuildCanonicalPayload(KeyId, Machine, new DateOnly(2026, 9, 29));
        var signature = ecdsa.SignData(
            Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var activate = new ActivateLicenseUseCase(
            new FakeMachineCodeProvider(Machine),
            new EcdsaLicenseSignatureVerifier(ecdsa.ExportSubjectPublicKeyInfo(), KeyId),
            new FakeLicenseProtector(), new DapperLicenseRepository(), bed.Units, bed.Clock);
        Assert.True((await activate.ExecuteAsync(
            new ActivateLicenseRequest(canonical + "." + Convert.ToBase64String(signature)), CancellationToken.None)).IsSuccess);

        // RN-LIC-006: una clave válida habilita permanentemente la instalación.
        var wiring = CreateWiring(bed, bed.Clock);
        var blocked = await wiring.Gate.CheckAsync(CancellationToken.None);

        Assert.Null(blocked);
    }

    [Fact]
    public async Task BlockedTrial_RefusesTheSale_AndBusinessDataStaysUntouched()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO \"User\" (UserId, DisplayName, Role, IsActive, PinSalt, PinHash, PinIterations, PinAlgorithmVersion)"
                + " VALUES (1, 'Admin', 'ADMIN', 1, @Salt, @Hash, 1, 1);",
                new { Salt = new byte[16], Hash = new byte[32] });
            await connection.ExecuteAsync(
                "INSERT INTO CashSession (OpenedAtUtcMs, OpenedAtOffsetMinutes, OpenedByUserId, OpenedByNameSnapshot, OpeningBalanceCop)"
                + " VALUES (1, 0, 1, 'Admin', 0);");
        }

        var create = new CreateProductUseCase(bed.Products, bed.Units, ProductTestBed.AdminSession());
        var created = await create.ExecuteAsync(
            new CreateProductRequest("M1", "Mango", ProductUnit.Kg, 6_000, 5_000), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);

        var finalize = new FinalizeSaleUseCase(bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, bed.Gate)
            .ExecuteAsync(
                new FinalizeSaleRequest(
                    [new DraftSaleLine(created.Value.ProductId, "M1", "Mango", ProductUnit.Kg, 6_000, 1_000)],
                    0, "CASH", 6_000),
                CancellationToken.None);
        Assert.True((await finalize).IsSuccess);

        // La prueba vence DESPUÉS de haber operado (RN-LIC-002).
        var nowMs = bed.Clock.UtcNow.ToUnixTimeMilliseconds();
        await using (var connection = new SqliteConnection($"Data Source={bed.DbPath}"))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("DELETE FROM TrialState;");
            await connection.ExecuteAsync(
                "INSERT INTO TrialState (TrialStateId, FirstRunUtcMs, LastSeenUtcMs) VALUES (1, @First, @Last);",
                new { First = nowMs - 40 * Day, Last = nowMs });
        }

        var wiring = CreateWiring(bed, bed.Clock);
        var gatedFinalize = new FinalizeSaleUseCase(
            bed.Sales, bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, wiring.Gate);
        var refused = await gatedFinalize.ExecuteAsync(
            new FinalizeSaleRequest(
                [new DraftSaleLine(created.Value.ProductId, "M1", "Mango", ProductUnit.Kg, 6_000, 1_000)],
                0, "CASH", 6_000),
            CancellationToken.None);

        Assert.True(refused.IsFailure);
        Assert.Equal(ErrorCode.LicenseBlocked, refused.Error!.Code);

        var gatedOpen = new OpenCashSessionUseCase(
            bed.Sessions, bed.Units, ProductTestBed.AdminSession(), bed.Clock, wiring.Gate);

        // La guarda se evalúa ANTES que cualquier otra regla de caja.
        var refusedOpen = await gatedOpen.ExecuteAsync(new OpenCashSessionRequest(10_000), CancellationToken.None);
        Assert.True(refusedOpen.IsFailure);
        Assert.Equal(ErrorCode.LicenseBlocked, refusedOpen.Error!.Code);

        // RN-LIC-003: el bloqueo jamás elimina ni modifica los datos del negocio.
        await using var verification = new SqliteConnection($"Data Source={bed.DbPath}");
        await verification.OpenAsync();
        Assert.Equal(1, await verification.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Sale;"));
        Assert.Equal(4_000, await verification.ExecuteScalarAsync<long>(
            "SELECT StockMilli FROM Product WHERE ProductId = @Id;", new { Id = created.Value.ProductId }));
        Assert.Equal("Mango", await verification.ExecuteScalarAsync<string>(
            "SELECT Name FROM Product WHERE ProductId = @Id;", new { Id = created.Value.ProductId }));
    }

    [Fact]
    public async Task Reinstall_KeepsOriginalFirstRunAndCountdown()
    {
        await using var bed = await ProductTestBed.CreateAsync();

        // Primera ejecución de la "instalación original".
        var first = CreateWiring(bed, bed.Clock);
        var initial = await first.Gate.CheckAsync(CancellationToken.None);
        Assert.Null(initial);

        // "Reinstalación": wiring completamente nuevo sobre la misma base
        // (ProgramData sobrevive) diez días después (RN-LIC-008).
        bed.Clock.UtcNow = bed.Clock.UtcNow.AddDays(10);
        var reinstalled = CreateWiring(bed, bed.Clock);
        var blocked = await reinstalled.Gate.CheckAsync(CancellationToken.None);

        Assert.Null(blocked); // la prueba no se reinicia: sigue activa, pero descontando

        var status = await reinstalled.Status.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);
        Assert.True(status.IsSuccess);
        Assert.False(status.Value.IsLicensed);

        await using var connection = new SqliteConnection($"Data Source={bed.DbPath}");
        await connection.OpenAsync();
        var stored = await connection.QuerySingleAsync<(long First, long Last)>(
            "SELECT FirstRunUtcMs AS First, LastSeenUtcMs AS Last FROM TrialState WHERE TrialStateId = 1;");
        Assert.Equal(bed.Clock.UtcNow.AddDays(-10).ToUnixTimeMilliseconds(), stored.First);
        Assert.Equal(bed.Clock.UtcNow.ToUnixTimeMilliseconds(), stored.Last);
    }
}
