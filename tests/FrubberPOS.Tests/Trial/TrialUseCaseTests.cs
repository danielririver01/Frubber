using System.Security.Cryptography;
using System.Text;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Trial;
using FrubberPOS.Domain.Licensing;
using FrubberPOS.Domain.Trial;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Persistence.Licensing;
using FrubberPOS.Infrastructure.Persistence.Trial;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;

namespace FrubberPOS.Tests.Trial;

/// <summary>
/// Proves the startup gate with a fake clock: first run starts 30 days,
/// expiry and backwards clocks block, licenses bypass everything.
/// Portado a PostgreSQL real (JHO-113).
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
        var clock = new FakeClock(T0);
        var wiring = await CreateWiringAsync(clock);

        var result = await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsLicensed);
        Assert.False(result.Value.IsBlocked);
        Assert.Equal(TrialOutcome.Active, result.Value.Outcome);
        Assert.Equal(30, result.Value.DaysLeft);

        var stored = await ReadStoredAsync();
        Assert.NotNull(stored);
        Assert.Equal(T0Ms, stored!.FirstRunUtcMs);
        Assert.Equal(T0Ms, stored.LastSeenUtcMs);
    }

    [Fact]
    public async Task SecondRun_AdvancesLastSeenAndCountsDown()
    {
        var clock = new FakeClock(T0);
        var wiring = await CreateWiringAsync(clock);
        await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

        clock.UtcNow = T0.AddDays(1);
        var second = await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(TrialOutcome.Active, second.Value.Outcome);
        Assert.Equal(29, second.Value.DaysLeft);
        Assert.Equal(T0Ms + Day, (await ReadStoredAsync())!.LastSeenUtcMs);
    }

    [Fact]
    public async Task ExactlyThirtyDays_BlocksAsExpired()
    {
        var clock = new FakeClock(T0);
        var wiring = await CreateWiringAsync(clock);
        await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

        clock.UtcNow = T0.AddDays(30);
        var result = await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsBlocked);
        Assert.Equal(TrialOutcome.Expired, result.Value.Outcome);
    }

    [Fact]
    public async Task ClockBack_BlocksWithoutTouchingState()
    {
        var clock = new FakeClock(T0.AddDays(10));
        var wiring = await CreateWiringAsync(clock);
        await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

        clock.UtcNow = T0.AddDays(9);
        var result = await wiring.Evaluate.ExecuteAsync(new EvaluateTrialRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsBlocked);
        Assert.Equal(TrialOutcome.ClockTampered, result.Value.Outcome);

        var stored = await ReadStoredAsync();
        Assert.Equal(T0Ms + 10 * Day, stored!.LastSeenUtcMs);
    }

    [Fact]
    public async Task Licensed_SkipsTrialEvenAfterExpiry()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var clock = new FakeClock(T0);
        var wiring = await CreateWiringAsync(clock, ecdsa);

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

    private sealed record Wiring(EvaluateTrialUseCase Evaluate, ActivateLicenseUseCase Activate);

    private static async Task<Wiring> CreateWiringAsync(FakeClock clock, ECDsa? ecdsa = null)
    {
        using var owned = ecdsa is null ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : null;
        var signer = ecdsa ?? owned!;
        await PostgresTestDb.MigrateAsync();
        await PostgresTestDb.CleanAllAsync();
        var factory = PostgresTestDb.NewUnitFactory();
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

    private static async Task<StoredTrial?> ReadStoredAsync()
    {
        await using var unit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        var stored = await new DapperTrialRepository().ReadAsync(unit, CancellationToken.None);
        await unit.CommitAsync(CancellationToken.None);
        return stored;
    }
}
