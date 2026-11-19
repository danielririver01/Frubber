using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using Dapper;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Workstations;
using FrubberPOS.Tests.TestDoubles;
using Npgsql;

namespace FrubberPOS.Tests.Infrastructure.Workstations;

public sealed class Postgres20WorkstationRegistrationTests
{
    private const string Server = "A123456789ABCDEF";

    [RegistrationFact]
    public async Task EnforcesSignedSeatsAndSerializesConcurrentRegistrations()
    {
        var options = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FRUBBER_JHO116_ADMIN")!);
        Assert.Equal("127.0.0.1", options.Host);
        Assert.NotEqual(5432, options.Port);
        Assert.Equal("frubber", options.Database);
        Assert.Equal(SslMode.VerifyFull, options.SslMode);
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM pg_roles WHERE rolname = 'frubber_puesto' OR rolname LIKE 'puesto_%';"));
        var installed = await new Postgres20BaselineInstaller().InstallAsync(
            new NpgsqlConnectionFactory(new DatabaseOptions(options.ConnectionString)), CancellationToken.None);
        Assert.True(installed.IsSuccess, installed.Error?.Message);
        var scripts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "database", "postgresql"));
        foreach (var script in new[] { "002_Security.sql", "012_PruebaNegocio.sql" })
            await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(scripts, script)));

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new EcdsaLicenseSignatureVerifier(key.ExportSubjectPublicKeyInfo(), "test");
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var today = clock.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var tomorrow = clock.UtcNow.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var registration = new Postgres20WorkstationRegistration(verifier, Server, clock);
        async Task SetLicense(string payload) => await connection.ExecuteAsync("""
            INSERT INTO public.license_state(license_state_id,payload_version,signed_payload)
            VALUES (1,1,@Payload) ON CONFLICT (license_state_id)
            DO UPDATE SET signed_payload = excluded.signed_payload;
            """, new { Payload = payload });
        string Sign(string canonical) => canonical + "." + Convert.ToBase64String(key.SignData(
            Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Task<Result<RegisteredWorkstation>> Register(string code) => registration.RegisterAsync(
            connection, "Caja de prueba", code, CancellationToken.None);

        await SetLicense(Sign($"FRB1|test|{Server}|{today}"));
        var first = await Register("B111111111111111");
        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.Equal(1, first.Value.Seats);
        Assert.Equal(1, first.Value.Active);
        var full = await Register("B222222222222222");
        Assert.Equal(ErrorCode.LicenseBlocked, full.Error?.Code);
        Assert.Contains("1 de 1", full.Error!.Message);

        foreach (var invalid in new[]
                 {
                     Sign($"FRB2|test|{Server}|{tomorrow}|2"),
                     Sign($"FRB2|test|D123456789ABCDEF|{today}|2"),
                     Sign($"FRB2|test|{Server}|{today}|2").Replace("|2.", "|3."),
                     "licencia inválida",
                 })
        {
            await SetLicense(invalid);
            Assert.Equal(ErrorCode.LicenseBlocked, (await Register("B222222222222222")).Error?.Code);
        }

        var signed = Sign($"FRB2|test|{Server}|{today}|2");
        await SetLicense(signed);
        Assert.Equal(ErrorCode.Conflict, (await Register("B111111111111111")).Error?.Code);
        Assert.Equal(ErrorCode.Validation, (await Register("0000000000000000")).Error?.Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registration.RegisterAsync(
            connection, "Cancelada", "B222222222222222", cancellation.Token));

        async Task<Result<RegisteredWorkstation>> ConcurrentRegister(string code)
        {
            await using var other = new NpgsqlConnection(options.ConnectionString);
            await other.OpenAsync();
            return await registration.RegisterAsync(other, "Otra caja", code, CancellationToken.None);
        }
        var attempts = await Task.WhenAll(ConcurrentRegister("B222222222222222"), ConcurrentRegister("B333333333333333"));
        var second = Assert.Single(attempts, result => result.IsSuccess);
        Assert.Equal(2, second.Value.Active);
        var rejected = Assert.Single(attempts, result => result.IsFailure);
        Assert.Equal(ErrorCode.LicenseBlocked, rejected.Error?.Code);
        Assert.Contains("2 de 2", rejected.Error!.Message);
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.terminal WHERE is_active;"));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_roles WHERE rolname LIKE 'puesto_%';"));
        Assert.Equal(signed, await connection.ExecuteScalarAsync<string>("SELECT signed_payload FROM public.license_state;"));

        // Deactivation frees a seat and preserves the previous terminal as history.
        await connection.ExecuteAsync("SELECT public.deactivate_terminal('B111111111111111');");
        var replacement = await Register("B444444444444444");
        Assert.True(replacement.IsSuccess, replacement.Error?.Message);
        Assert.Equal(2, replacement.Value.Active);
        Assert.Equal(3, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.terminal WHERE machine_code <> '0000000000000000';"));
    }

    private sealed class RegistrationFactAttribute : FactAttribute
    {
        public RegistrationFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("FRUBBER_JHO136_ISOLATED") != "1" ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FRUBBER_JHO116_ADMIN")))
                Skip = "Requires an explicitly enabled disposable TLS PostgreSQL cluster.";
        }
    }
}
