using System.Security.Cryptography;
using System.Text;
using Dapper;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Trial;
using FrubberPOS.Domain.Licensing;
using FrubberPOS.Domain.Trial;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Licensing;
using FrubberPOS.Infrastructure.Persistence.Trial;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Trial;

/// <summary>
/// Proves the startup gate with a fake clock: first run starts 30 days,
/// expiry and backwards clocks block, licenses bypass everything.
/// </summary>
public sealed class TrialUseCaseTests
{
    private const string KeyId = "lic-test";
    private const string Machine = "0123456789ABCDEF";
    private const long Day = 86_400_000L;

    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly long T0Ms = T0.ToUnixTimeMilliseconds();

    [Fact]
    public async Task FirstRun_StartsThirtyDayTrial()
    {
        var path = NewTempDbPath();
        try
        {
            var clock = new FakeClock(T0);
            var wiring = await CreateWiringAsync(path, clock);

            var result = await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.IsLicensed);
            Assert.False(result.Value.IsBlocked);
            Assert.Equal(TrialOutcome.Active, result.Value.Outcome);
            Assert.Equal(30, result.Value.DaysLeft);

            var stored = await ReadStoredAsync(path);
            Assert.NotNull(stored);
            Assert.Equal(T0Ms, stored.FirstRunUtcMs);
            Assert.Equal(T0Ms, stored.LastSeenUtcMs);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task SecondRun_AdvancesLastSeenAndCountsDown()
    {
        var path = NewTempDbPath();
        try
        {
            var clock = new FakeClock(T0);
            var wiring = await CreateWiringAsync(path, clock);
            await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

            clock.UtcNow = T0.AddDays(1);
            var second = await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

            Assert.True(second.IsSuccess);
            Assert.Equal(TrialOutcome.Active, second.Value.Outcome);
            Assert.Equal(29, second.Value.DaysLeft);
            Assert.Equal(T0Ms + Day, (await ReadStoredAsync(path))!.LastSeenUtcMs);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task ExactlyThirtyDays_BlocksAsExpired()
    {
        var path = NewTempDbPath();
        try
        {
            var clock = new FakeClock(T0);
            var wiring = await CreateWiringAsync(path, clock);
            await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

            clock.UtcNow = T0.AddDays(30);
            var result = await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.IsBlocked);
            Assert.Equal(TrialOutcome.Expired, result.Value.Outcome);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task ClockBack_BlocksWithoutTouchingState()
    {
        var path = NewTempDbPath();
        try
        {
            var clock = new FakeClock(T0.AddDays(10));
            var wiring = await CreateWiringAsync(path, clock);
            await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

            clock.UtcNow = T0.AddDays(9);
            var result = await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.IsBlocked);
            Assert.Equal(TrialOutcome.ClockTampered, result.Value.Outcome);

            var stored = await ReadStoredAsync(path);
            Assert.Equal(T0Ms + 10 * Day, stored!.LastSeenUtcMs);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task Licensed_SkipsTrialEvenAfterExpiry()
    {
        var path = NewTempDbPath();
        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var clock = new FakeClock(T0);
            var wiring = await CreateWiringAsync(path, clock, ecdsa);

            var activated = await wiring.Activate.ExecuteAsync(
                new ActivateLicenseRequest(BuildLicenseText(new DateOnly(2026, 9, 29), ecdsa)),
                CancellationToken.None);
            Assert.True(activated.IsSuccess);

            clock.UtcNow = T0.AddDays(60);
            var result = await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.IsLicensed);
            Assert.False(result.Value.IsBlocked);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    private sealed record Wiring(EvaluateTrialUseCase Evaluate, ActivateLicenseUseCase Activate);

    private static async Task<Wiring> CreateWiringAsync(string path, FakeClock clock, ECDsa? ecdsa = null)
    {
        using var owned = ecdsa is null ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : null;
        var signer = ecdsa ?? owned!;
        var factory = await OpenFactoryAsync(path);
        var provider = new FakeMachineCodeProvider(Machine);
        var protector = new FakeLicenseProtector();
        var licenses = new DapperLicenseRepository();
        var verifier = new EcdsaLicenseSignatureVerifier(signer.ExportSubjectPublicKeyInfo(), KeyId);

        var status = new GetLicenseStatusUseCase(provider, protector, licenses, factory);
        return new Wiring(
            new EvaluateTrialUseCase(status, new DapperTrialRepository(), factory, clock),
            new ActivateLicenseUseCase(provider, verifier, protector, licenses, factory, clock));
    }

    private static string BuildLicenseText(DateOnly date, ECDsa ecdsa)
    {
        var canonical = LicenseKeyFormat.BuildCanonicalPayload(KeyId, Machine, date);
        var signature = ecdsa.SignData(
            Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return canonical + "." + Convert.ToBase64String(signature);
    }

    private static async Task<SqliteUnitOfWorkFactory> OpenFactoryAsync(string path)
    {
        var factory = new SqliteUnitOfWorkFactory(new SqliteConnectionFactory(new DatabaseOptions(path)));
        await EnsureTablesAsync(factory);
        return factory;
    }

    private static async Task EnsureTablesAsync(SqliteUnitOfWorkFactory factory)
    {
        // Minimal TrialState + LicenseState DDL matching the migrations.
        const string ddl = "CREATE TABLE IF NOT EXISTS TrialState ("
            + "TrialStateId INTEGER PRIMARY KEY CHECK (TrialStateId = 1),"
            + "FirstRunUtcMs INTEGER NOT NULL CHECK (FirstRunUtcMs > 0),"
            + "LastSeenUtcMs INTEGER NOT NULL CHECK (LastSeenUtcMs > 0)) STRICT;"
            + "CREATE TABLE IF NOT EXISTS LicenseState ("
            + "LicenseStateId INTEGER PRIMARY KEY CHECK (LicenseStateId = 1),"
            + "PayloadVersion INTEGER NOT NULL CHECK (PayloadVersion > 0),"
            + "ProtectedPayload BLOB NOT NULL CHECK (length(ProtectedPayload) > 0),"
            + "UpdatedAtUtcMs INTEGER NOT NULL CHECK (UpdatedAtUtcMs > 0)) STRICT;";
        await using var unit = await factory.CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
        await unit.Connection.ExecuteAsync(ddl, transaction: unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);
    }

    private static async Task<StoredTrial?> ReadStoredAsync(string path)
    {
        var factory = await OpenFactoryAsync(path);
        await using var unit = await factory.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        var stored = await new DapperTrialRepository().ReadAsync(unit, CancellationToken.None);
        await unit.CommitAsync(CancellationToken.None);
        return stored;
    }

    private static string NewTempDbPath() =>
        Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteTempDb(string path)
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        foreach (var journal in new[] { path + "-wal", path + "-shm", path + "-journal" })
        {
            if (File.Exists(journal))
            {
                File.Delete(journal);
            }
        }
    }
}
