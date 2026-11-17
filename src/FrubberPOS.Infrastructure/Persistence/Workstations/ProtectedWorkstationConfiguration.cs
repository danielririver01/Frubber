using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using Npgsql;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

[SupportedOSPlatform("windows")]
public sealed class ProtectedWorkstationConfiguration
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FrubberPOS.Workstation.v2");

    private ProtectedWorkstationConfiguration(DatabaseOptions database, string serverMachineCode)
    {
        Database = database;
        ServerMachineCode = serverMachineCode;
    }

    public DatabaseOptions Database { get; }
    public string ServerMachineCode { get; }

    public static async Task<Result<ProtectedWorkstationConfiguration>> LoadAsync(
        string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(fileName, cancellationToken);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var config = JsonSerializer.Deserialize<ConfigurationData>(plain);
                if (config is null || string.IsNullOrWhiteSpace(config.ConnectionString) ||
                    config.ServerMachineCode is null || !Regex.IsMatch(config.ServerMachineCode, @"\A[0-9A-F]{16}\z") ||
                    config.ServerMachineCode == "0000000000000000")
                {
                    return InvalidConfiguration();
                }

                var connection = new NpgsqlConnectionStringBuilder(config.ConnectionString);
                if (connection.SslMode != SslMode.VerifyFull ||
                    string.IsNullOrEmpty(connection.Username) || !Regex.IsMatch(connection.Username, @"\Apuesto_[0-9a-f]{16}\z") ||
                    string.IsNullOrEmpty(connection.Password))
                {
                    return InvalidConfiguration();
                }

                connection.PersistSecurityInfo = false;
                connection.IncludeErrorDetail = false;
                return Result<ProtectedWorkstationConfiguration>.Success(
                    new(new DatabaseOptions(connection.ConnectionString), config.ServerMachineCode));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException)
        {
            return InvalidConfiguration();
        }
    }

    private static Result<ProtectedWorkstationConfiguration> InvalidConfiguration() =>
        Result<ProtectedWorkstationConfiguration>.Failure(new Error(ErrorCode.Authorization,
            "No se pudo leer la configuración protegida de este puesto. Solicite al administrador que lo configure."));

    private sealed record ConfigurationData(string ConnectionString, string ServerMachineCode);
}
