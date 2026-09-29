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
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Licensing;

/// <summary>
/// Proves activation accepts only genuine team-bound keys without changing
/// state on rejection, and that status reports fail closed.
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
        var path = NewTempDbPath();
        try
        {
            var wiring = await CreateWiringAsync(path, Machine);
            var result = await wiring.Activate.ExecuteAsync(
                new ActivateLicenseRequest("no-es-una-licencia"), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCode.Validation, result.Error!.Code);
            Assert.Null(await ReadStoredAsync(path));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task Activate_WrongMachine_FailsAndStoresNothing()
    {
        var path = NewTempDbPath();
        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var wiring = await CreateWiringAsync(path, Machine, ecdsa);
            var foreign = BuildLicenseText(OtherMachine, new DateOnly(2026, 9, 29), ecdsa);

            var result = await wiring.Activate.ExecuteAsync(new ActivateLicenseRequest(foreign), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCode.Validation, result.Error!.Code);
            Assert.Null(await ReadStoredAsync(path));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task Activate_BadSignature_FailsAndStoresNothing()
    {
        var path = NewTempDbPath();
        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var wiring = await CreateWiringAsync(path, Machine, ecdsa);
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var forged = BuildLicenseText(Machine, new DateOnly(2026, 9, 29), other);

            var result = await wiring.Activate.ExecuteAsync(new ActivateLicenseRequest(forged), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCode.Validation, result.Error!.Code);
            Assert.Null(await ReadStoredAsync(path));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task Activate_FutureIssueDate_FailsAndStoresNothing()
    {
        var path = NewTempDbPath();
        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var wiring = await CreateWiringAsync(path, Machine, ecdsa);
            var future = BuildLicenseText(Machine, new DateOnly(2026, 10, 15), ecdsa);

            var result = await wiring.Activate.ExecuteAsync(new ActivateLicenseRequest(future), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCode.Validation, result.Error!.Code);
            Assert.Null(await ReadStoredAsync(path));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task Activate_Valid_StoresSealedPayloadAndReportsLicensed()
    {
        var path = NewTempDbPath();
        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var wiring = await CreateWiringAsync(path, Machine, ecdsa);
            var text = BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa);

            var result = await wiring.Activate.ExecuteAsync(new ActivateLicenseRequest(text), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.IsLifetime);

            var stored = await ReadStoredAsync(path);
            Assert.NotNull(stored);
            Assert.Equal(ActivateLicenseUseCase.PayloadVersion, stored.PayloadVersion);
            Assert.NotEqual(
                Encoding.UTF8.GetBytes(LicenseKeyFormat.BuildCanonicalPayload(KeyId, Machine, new DateOnly(2026, 9, 29))),
                stored.ProtectedPayload);

            var status = await wiring.Status.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);
            Assert.True(status.IsSuccess);
            Assert.True(status.Value.IsLicensed);
            Assert.Equal("0123-4567-89AB-CDEF", status.Value.MachineDisplayCode);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task Activate_Twice_KeepsSingleRow()
    {
        var path = NewTempDbPath();
        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var wiring = await CreateWiringAsync(path, Machine, ecdsa);

            await wiring.Activate.ExecuteAsync(
                new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa)),
                CancellationToken.None);
            var second = await wiring.Activate.ExecuteAsync(
                new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 9, 28), ecdsa)),
                CancellationToken.None);

            Assert.True(second.IsSuccess);
            Assert.Equal(1, await RowCountAsync(path));
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task Status_NoRow_ReportsUnlicensed()
    {
        var path = NewTempDbPath();
        try
        {
            var wiring = await CreateWiringAsync(path, Machine);

            var status = await wiring.Status.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);

            Assert.True(status.IsSuccess);
            Assert.False(status.Value.IsLicensed);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task Status_TamperedPayload_ReportsUnlicensed()
    {
        var path = NewTempDbPath();
        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var wiring = await CreateWiringAsync(path, Machine, ecdsa);
            await wiring.Activate.ExecuteAsync(
                new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa)),
                CancellationToken.None);

            await TamperStoredPayloadAsync(path);

            var status = await wiring.Status.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);

            Assert.True(status.IsSuccess);
            Assert.False(status.Value.IsLicensed);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    [Fact]
    public async Task Status_OtherMachine_ReportsUnlicensed()
    {
        var path = NewTempDbPath();
        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var activated = await CreateWiringAsync(path, Machine, ecdsa);
            await activated.Activate.ExecuteAsync(
                new ActivateLicenseRequest(BuildLicenseText(Machine, new DateOnly(2026, 9, 29), ecdsa)),
                CancellationToken.None);

            var foreignWiring = await CreateWiringAsync(path, OtherMachine, ecdsa);
            var status = await foreignWiring.Status.ExecuteAsync(new GetLicenseStatusRequest(), CancellationToken.None);

            Assert.True(status.IsSuccess);
            Assert.False(status.Value.IsLicensed);
        }
        finally
        {
            DeleteTempDb(path);
        }
    }

    private sealed record Wiring(
        ActivateLicenseUseCase Activate,
        GetLicenseStatusUseCase Status,
        GetMachineCodeUseCase MachineCode);

    private static async Task<Wiring> CreateWiringAsync(string path, string machine, ECDsa? ecdsa = null)
    {
        using var owned = ecdsa is null ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : null;
        var signer = ecdsa ?? owned!;
        var verifier = new EcdsaLicenseSignatureVerifier(signer.ExportSubjectPublicKeyInfo(), KeyId);

        var factory = await OpenFactoryAsync(path);
        var clock = new FakeClock(Now);
        var licenses = new DapperLicenseRepository();
        var provider = new FakeMachineCodeProvider(machine);
        var protector = new FakeLicenseProtector();

        return new Wiring(
            new ActivateLicenseUseCase(provider, verifier, protector, licenses, factory, clock),
            new GetLicenseStatusUseCase(provider, protector, licenses, factory),
            new GetMachineCodeUseCase(provider));
    }

    private static string BuildLicenseText(string machine, DateOnly date, ECDsa ecdsa)
    {
        var canonical = LicenseKeyFormat.BuildCanonicalPayload(KeyId, machine, date);
        var signature = ecdsa.SignData(
            Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return canonical + "." + Convert.ToBase64String(signature);
    }

    private static async Task<SqliteUnitOfWorkFactory> OpenFactoryAsync(string path)
    {
        var factory = new SqliteUnitOfWorkFactory(new SqliteConnectionFactory(new DatabaseOptions(path)));
        await EnsureTableAsync(factory);
        return factory;
    }

    private static async Task EnsureTableAsync(SqliteUnitOfWorkFactory factory)
    {
        // Minimal LicenseState DDL matching 001_Initial.sql for these tests.
        const string ddl = "CREATE TABLE IF NOT EXISTS LicenseState ("
            + "LicenseStateId INTEGER PRIMARY KEY CHECK (LicenseStateId = 1),"
            + "PayloadVersion INTEGER NOT NULL CHECK (PayloadVersion > 0),"
            + "ProtectedPayload BLOB NOT NULL CHECK (length(ProtectedPayload) > 0),"
            + "UpdatedAtUtcMs INTEGER NOT NULL CHECK (UpdatedAtUtcMs > 0)) STRICT;";
        await using var unit = await factory.CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
        await unit.Connection.ExecuteAsync(ddl, transaction: unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);
    }

    private static async Task<StoredLicense?> ReadStoredAsync(string path)
    {
        var factory = await OpenFactoryAsync(path);
        await using var unit = await factory.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        var stored = await new DapperLicenseRepository().ReadAsync(unit, CancellationToken.None);
        await unit.CommitAsync(CancellationToken.None);
        return stored;
    }

    private static async Task TamperStoredPayloadAsync(string path)
    {
        var factory = await OpenFactoryAsync(path);
        await using var unit = await factory.CreateAsync(UnitOfWorkMode.Write, CancellationToken.None);
        var stored = (await new DapperLicenseRepository().ReadAsync(unit, CancellationToken.None))!;
        stored.ProtectedPayload[0] ^= 0xFF;
        await unit.Connection.ExecuteAsync(
            "UPDATE LicenseState SET ProtectedPayload = @Payload WHERE LicenseStateId = 1;",
            new { Payload = stored.ProtectedPayload },
            unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);
    }

    private static async Task<long> RowCountAsync(string path)
    {
        var factory = await OpenFactoryAsync(path);
        await using var unit = await factory.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None);
        var count = await unit.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM LicenseState;", transaction: unit.Transaction);
        await unit.CommitAsync(CancellationToken.None);
        return count;
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
