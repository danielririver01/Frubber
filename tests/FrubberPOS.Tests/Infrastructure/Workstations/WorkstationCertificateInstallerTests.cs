using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Dapper;
using FrubberPOS.Application.Common;
using FrubberPOS.Infrastructure.Persistence.Workstations;
using Npgsql;

namespace FrubberPOS.Tests.Infrastructure.Workstations;

public sealed class WorkstationCertificateInstallerTests
{
    [CertificateFact]
    public async Task InstallsCertificateAndProtectedProfileAndPreservesPreviousProfileOnFailure()
    {
        var options = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FRUBBER_JHO129_ADMIN")!);
        Assert.Equal("127.0.0.1", options.Host);
        Assert.NotEqual(5432, options.Port);
        Assert.Equal(SslMode.VerifyFull, options.SslMode);
        await using var admin = new NpgsqlConnection(options.ConnectionString);
        await admin.OpenAsync();
        Assert.Equal("on", await admin.ExecuteScalarAsync<string>("SHOW ssl;"));
        var code = Guid.NewGuid().ToString("N")[..16];
        var role = "puesto_" + code;
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await admin.ExecuteAsync($"CREATE ROLE {role} LOGIN PASSWORD '{password}';");
        var directory = Directory.CreateTempSubdirectory("frubber-certificate-");
        try
        {
            var workstation = new NpgsqlConnectionStringBuilder(options.ConnectionString)
            {
                Username = role,
                Password = password,
                RootCertificate = "original-certificate-unavailable.pem",
            };
            var source = Path.Combine(directory.FullName, "source.cfg");
            var target = Path.Combine(directory.FullName, "installed");
            async Task WriteProfile(string server) => await File.WriteAllBytesAsync(source,
                ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    ConnectionString = workstation.ConnectionString,
                    ServerMachineCode = server,
                }), Encoding.UTF8.GetBytes("FrubberPOS.Workstation.v2"), DataProtectionScope.CurrentUser));
            await WriteProfile("A123456789ABCDEF");
            var original = await File.ReadAllBytesAsync(source);
            var installer = new WorkstationCertificateInstaller();
            var installed = await installer.InstallAsync(source, options.RootCertificate!, target, CancellationToken.None);
            Assert.True(installed.IsSuccess, installed.Error?.Message);
            var profile = await ProtectedWorkstationConfiguration.LoadAsync(installed.Value, CancellationToken.None);
            Assert.True(profile.IsSuccess, profile.Error?.Message);
            var configured = new NpgsqlConnectionStringBuilder(profile.Value.Database.ConnectionString);
            Assert.Equal(SslMode.VerifyFull, configured.SslMode);
            Assert.Equal(Path.Combine(target, "connection"), Path.GetDirectoryName(configured.RootCertificate));
            Assert.True(File.Exists(configured.RootCertificate));
            await using (var connected = new NpgsqlConnection(configured.ConnectionString))
            {
                await connected.OpenAsync();
                Assert.Equal("on", await connected.ExecuteScalarAsync<string>("SHOW ssl;"));
            }
            Assert.Equal(original, await File.ReadAllBytesAsync(source));
            var previous = await File.ReadAllBytesAsync(installed.Value);

            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Other root", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var wrongCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var wrongFile = Path.Combine(directory.FullName, "wrong.pem");
            await File.WriteAllTextAsync(wrongFile, wrongCertificate.ExportCertificatePem());
            Assert.True((await installer.InstallAsync(source, wrongFile, target, CancellationToken.None)).IsFailure);
            Assert.Equal(previous, await File.ReadAllBytesAsync(installed.Value));

            var privateFile = Path.Combine(directory.FullName, "private.pem");
            await File.WriteAllTextAsync(privateFile, wrongCertificate.ExportCertificatePem() + key.ExportPkcs8PrivateKeyPem());
            Assert.Equal(ErrorCode.Validation, (await installer.InstallAsync(source, privateFile, target, CancellationToken.None)).Error?.Code);
            Assert.Equal(previous, await File.ReadAllBytesAsync(installed.Value));

            Assert.True((await installer.InstallAsync(source, options.RootCertificate!, target, CancellationToken.None)).IsSuccess);
            var current = await File.ReadAllBytesAsync(installed.Value);
            await using (var busy = new FileStream(Path.Combine(target, "connection", "install.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                Assert.True((await installer.InstallAsync(source, options.RootCertificate!, target, CancellationToken.None)).IsFailure);
                Assert.Equal(current, await File.ReadAllBytesAsync(installed.Value));
            }
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                installer.InstallAsync(source, options.RootCertificate!, target, canceled.Token));
            Assert.Equal(current, await File.ReadAllBytesAsync(installed.Value));
            await WriteProfile("B123456789ABCDEF");
            Assert.Equal(ErrorCode.Conflict, (await installer.InstallAsync(source, options.RootCertificate!, target, CancellationToken.None)).Error?.Code);
            Assert.Equal(current, await File.ReadAllBytesAsync(installed.Value));
            await File.WriteAllTextAsync(source, "invalid protected profile");
            Assert.True((await installer.InstallAsync(source, options.RootCertificate!, target, CancellationToken.None)).IsFailure);
            Assert.Equal(current, await File.ReadAllBytesAsync(installed.Value));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
            await admin.ExecuteAsync($"DROP ROLE {role};");
        }
    }

    private sealed class CertificateFactAttribute : FactAttribute
    {
        public CertificateFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("FRUBBER_JHO136_ISOLATED") != "1" ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FRUBBER_JHO129_ADMIN")))
                Skip = "Requires an explicitly enabled disposable TLS PostgreSQL cluster.";
        }
    }
}
