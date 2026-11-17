using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FrubberPOS.Application.Common;
using Npgsql;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

[SupportedOSPlatform("windows")]
public sealed class WorkstationCertificateInstaller
{
    public async Task<Result<string>> InstallAsync(
        string sourceProfile, string certificateFile, string targetDirectory, CancellationToken cancellationToken)
    {
        var loaded = await ProtectedWorkstationConfiguration.LoadAsync(sourceProfile, cancellationToken);
        if (loaded.IsFailure)
            return Result<string>.Failure(loaded.Error!);

        try
        {
            var pem = await File.ReadAllTextAsync(certificateFile, cancellationToken);
            if (pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
                return InvalidCertificate();
            using var certificate = X509Certificate2.CreateFromPem(pem);
            var publicPem = certificate.ExportCertificatePem();
            var hash = Convert.ToHexString(SHA256.HashData(certificate.RawData));
            var directory = Path.Combine(Path.GetFullPath(targetDirectory), "connection");
            Directory.CreateDirectory(directory);
            await using var installLock = new FileStream(Path.Combine(directory, "install.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            var installedCertificate = Path.Combine(directory, $"server-{hash}.pem");
            // Content-addressed certificates preserve the previous profile during an update.
            var temporaryCertificate = Path.Combine(directory, $"certificate-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporaryCertificate, publicPem, cancellationToken);
                if (!File.Exists(installedCertificate))
                    File.Move(temporaryCertificate, installedCertificate);
                if (await File.ReadAllTextAsync(installedCertificate, cancellationToken) != publicPem)
                    return InvalidCertificate();
            }
            finally
            {
                if (File.Exists(temporaryCertificate))
                    File.Delete(temporaryCertificate);
            }

            var options = new NpgsqlConnectionStringBuilder(loaded.Value.Database.ConnectionString)
            {
                RootCertificate = installedCertificate,
                SslMode = SslMode.VerifyFull,
            };
            var verification = new NpgsqlConnectionStringBuilder(options.ConnectionString) { Pooling = false };
            await using (var connection = new NpgsqlConnection(verification.ConnectionString))
                await connection.OpenAsync(cancellationToken);

            var destination = Path.Combine(directory, "puesto.cfg");
            if (File.Exists(destination))
            {
                var previous = await ProtectedWorkstationConfiguration.LoadAsync(destination, cancellationToken);
                if (previous.IsFailure || previous.Value.ServerMachineCode != loaded.Value.ServerMachineCode ||
                    new NpgsqlConnectionStringBuilder(previous.Value.Database.ConnectionString).Username != options.Username)
                    return Result<string>.Failure(new(ErrorCode.Conflict,
                        "La carpeta contiene la configuración de otro puesto. Elija otra ubicación."));
            }

            var plain = JsonSerializer.SerializeToUtf8Bytes(new
            {
                ConnectionString = options.ConnectionString,
                loaded.Value.ServerMachineCode,
            });
            byte[] protectedBytes;
            try
            {
                protectedBytes = ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("FrubberPOS.Workstation.v2"),
                    DataProtectionScope.CurrentUser);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }

            var temporary = Path.Combine(directory, $"puesto-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            return Result<string>.Success(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or
                                   NpgsqlException or ArgumentException)
        {
            return Result<string>.Failure(new(ErrorCode.Connection,
                "No se pudo configurar la conexión segura del puesto. Compruebe el certificado, el servidor y la cuenta de Windows del perfil."));
        }
    }

    private static Result<string> InvalidCertificate() => Result<string>.Failure(new(ErrorCode.Validation,
        "Seleccione el certificado público del servidor en formato PEM."));
}
