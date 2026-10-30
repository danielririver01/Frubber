using System.Security.Cryptography;
using System.Text;
using Dapper;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Domain.Licensing;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Licensing;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.TestInfrastructure;

namespace FrubberPOS.Tests.Licensing;

/// <summary>
/// Proves activation accepts only genuine team-bound keys without changing
/// state on rejection, and that status reports fail closed. (Portado a
/// PostgreSQL en JHO-113: base de prueba real compartida.)
/// </summary>
public sealed class LicenseUseCaseTests
{
    private const string KeyId = "lic-test";
    private const string Machine = "0123456789ABCDEF";
    private const string OtherMachine = "FFFFFFFFFFFFFFFF";

    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetMachineCode_ReturnsGroupedCode()
    {
        var useCase = new GetMachineCodeUseCase(new FakeMachineCodeProvider(Machine, "AB**12:hash"));

        var result = await useCase.ExecuteAsync(new GetMachineCodeRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("0123-4567-89AB-CDEF", result.Value.DisplayCode);
    }

    [Fact]
    public async Task Activate_InvalidFormat_FailsAndStoresNothing()
    {
        var wiring = await CreateWiringAsync(Machine);
        var result = await wiring.Activate.ExecuteAsync(
            new ActivateLicenseRequest("no-es-una-licencia"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Null(await ReadStoredAsync());
    }

    [Fact]
    public async Task Activate_WrongMachine_FailsAndStoresNothing()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wiring = await CreateWiringAsync(Machine, ecdsa);
        var result = await wiring.Activate.ExecuteAsync(
            new ActivateLicenseRequest(BuildLicenseText(OtherMachine, new DateOnly(2026, 9, 29), ecdsa)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(await ReadStoredAsync());
    }

    [Fact]
    public async Task Activate_WrongKeyId_FailsAndStoresNothing()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wiring = await CreateWiringAsync(Machine, ecdsa);
        var result = await wiring.Activate.ExecuteAsync(
            new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa, "otra-llave")),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(await ReadStoredAsync());
    }

    [Fact]
    public async Task Activate_FutureIssueDate_FailsAndStoresNothing()
    {
        // JHO-113: la fecha va más allá de la tolerancia de un día que aplica
        // el caso de uso (IssuedDate > today + 1) — 2026-09-30 quedaría dentro
        // de esa tolerancia con el reloj en 2026-09-29 y la activación pasaría.
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wiring = await CreateWiringAsync(Machine, ecdsa);
        var result = await wiring.Activate.ExecuteAsync(
            new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 10, 15), ecdsa)),
            CancellationToken.None);

        Assert.True(result.IsFailure, "Una llave con fecha de emisión futura debe rechazarse con el reloj en pasado.");
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Null(await ReadStoredAsync());
    }

    [Fact]
    public async Task Activate_Valid_StoresSealedPayloadAndReportsLicensed()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wiring = await CreateWiringAsync(Machine, ecdsa);

        var result = await wiring.Activate.ExecuteAsync(
            new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.IsLifetime);
        var stored = await ReadStoredAsync();
        Assert.NotNull(stored);
        Assert.Equal(ActivateLicenseUseCase.PayloadVersion, stored!.PayloadVersion);
    }

    [Fact]
    public async Task Activate_Twice_KeepsSingleRow()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wiring = await CreateWiringAsync(Machine, ecdsa);
        var activatedFirst = new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa));
        Assert.True((await wiring.Activate.ExecuteAsync(activatedFirst, CancellationToken.None)).IsSuccess);

        Assert.True((await wiring.Activate.ExecuteAsync(activatedFirst, CancellationToken.None)).IsSuccess);
        Assert.Equal(1, await RowCountAsync());
    }

    [Fact]
    public async Task Status_NoLicense_ReportsUnlicensed()
    {
        var wiring = await CreateWiringAsync(Machine);
        var status = await wiring.Status.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);

        Assert.True(status.IsSuccess);
        Assert.False(status.Value.IsLicensed);
    }

    [Fact]
    public async Task Status_AfterValidActivation_ReportsLicensed()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wiring = await CreateWiringAsync(Machine, ecdsa);
        Assert.True((await wiring.Activate.ExecuteAsync(
            new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa)),
            CancellationToken.None)).IsSuccess);

        var status = await wiring.Status.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);

        Assert.True(status.IsSuccess);
        Assert.True(status.Value.IsLicensed);
    }

    [Fact]
    public async Task Status_TamperedPayload_ReportsUnlicensed()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wiring = await CreateWiringAsync(Machine, ecdsa);
        await wiring.Activate.ExecuteAsync(
            new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa)),
            CancellationToken.None);

        await TamperStoredPayloadAsync();

        var status = await wiring.Status.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);

        Assert.True(status.IsSuccess);
        Assert.False(status.Value.IsLicensed);
    }

    [Fact]
    public async Task Status_OtherMachine_ReportsUnlicensed()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var activated = await CreateWiringAsync(Machine, ecdsa);
        await activated.Activate.ExecuteAsync(
            new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa)),
            CancellationToken.None);

        var foreignWiring = await CreateWiringAsync(OtherMachine, ecdsa);
        var status = await foreignWiring.Status.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);

        Assert.True(status.IsSuccess);
        Assert.False(status.Value.IsLicensed);
    }

    private sealed record Wiring(
        ActivateLicenseUseCase Activate,
        GetLicenseStatusUseCase Status,
        GetMachineCodeUseCase MachineCode);

    private static async Task<Wiring> CreateWiringAsync(string machine, ECDsa? ecdsa = null)
    {
        using var owned = ecdsa is null ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : null;
        var signer = ecdsa ?? owned!;
        var verifier = new EcdsaLicenseSignatureVerifier(signer.ExportSubjectPublicKeyInfo(), KeyId);

        await PostgresTestDb.MigrateAsync();
        await PostgresTestDb.CleanAllAsync();
        var factory = PostgresTestDb.NewUnitFactory();
        var clock = new FakeClock(Now);
        var licenses = new DapperLicenseRepository();
        var provider = new FakeMachineCodeProvider(machine);
        var protector = new FakeLicenseProtector();

        return new Wiring(
            new ActivateLicenseUseCase(provider, verifier, protector, licenses, factory, clock),
            new GetLicenseStatusUseCase(provider, protector, licenses, factory),
            new GetMachineCodeUseCase(provider));
    }

    private static string BuildLicenseText(string machine, DateOnly date, ECDsa ecdsa, string? keyId = null)
    {
        var canonical = LicenseKeyFormat.BuildCanonicalPayload(keyId ?? KeyId, machine, date);
        var signature = ecdsa.SignData(
            Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return canonical + "." + Convert.ToBase64String(signature);
    }

    private static async Task<StoredLicense?> ReadStoredAsync()
    {
        await using var unit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        var stored = await new DapperLicenseRepository().ReadAsync(unit, CancellationToken.None);
        await unit.CommitAsync(CancellationToken.None);
        return stored;
    }

    private static async Task TamperStoredPayloadAsync()
    {
        await using var unit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.WriteImmediate, CancellationToken.None);
        var stored = (await new DapperLicenseRepository().ReadAsync(unit, CancellationToken.None))!;
        stored.ProtectedPayload[0] ^= 0xFF;
        await unit.Connection.ExecuteAsync(
            "UPDATE LicenseState SET ProtectedPayload = @Payload WHERE LicenseStateId = 1;",
            new { Payload = stored.ProtectedPayload },
            unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);
    }

    private static async Task<long> RowCountAsync()
    {
        await using var unit = await PostgresTestDb.NewUnitFactory().CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        var count = await unit.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM LicenseState;");
        await unit.CommitAsync(CancellationToken.None);
        return count;
    }
}
