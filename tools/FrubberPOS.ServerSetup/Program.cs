using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Workstations;
using FrubberPOS.Infrastructure.Security;
using FrubberPOS.Infrastructure.System;
using Npgsql;

try
{
    if (args is ["certificado", var directory, var address] &&
        IPAddress.TryParse(address, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
    {
        CreateCertificate(directory, ip);
        return 0;
    }

    if (args is ["inicializar", var portText, var certificate, var profile] &&
        ushort.TryParse(portText, out var port) && port > 0)
    {
        var password = Console.ReadLine();
        var pin = Console.ReadLine();
        if (string.IsNullOrEmpty(password) || pin is null || pin.Length != 4 || !pin.All(char.IsAsciiDigit))
        {
            Console.Error.WriteLine("Se requiere la contraseña del motor y un PIN de cuatro dígitos.");
            return 1;
        }

        await InitializeAsync(port, Path.GetFullPath(certificate), profile, password, pin);
        Console.WriteLine("Base frubber, administrador inicial y perfil protegido preparados.");
        return 0;
    }

    Console.Error.WriteLine("Use el asistente de Caja principal para preparar el servidor.");
    return 1;
}
catch (PostgresException ex)
{
    Console.Error.WriteLine($"PostgreSQL rechazó la preparación del servidor (SQLSTATE {ex.SqlState}).");
    return 1;
}
catch (Exception ex) when (ex is NpgsqlException or IOException or UnauthorizedAccessException
                           or CryptographicException or InvalidOperationException)
{
    Console.Error.WriteLine("No se pudo preparar la base, el certificado o el perfil. Revise los registros del asistente.");
    return 1;
}

static void CreateCertificate(string directory, IPAddress lanAddress)
{
    using var rsa = RSA.Create(3072);
    var request = new CertificateRequest("CN=Frubber POS Servidor", rsa, HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    var names = new SubjectAlternativeNameBuilder();
    names.AddIpAddress(IPAddress.Loopback);
    if (!lanAddress.Equals(IPAddress.Loopback))
        names.AddIpAddress(lanAddress);
    request.CertificateExtensions.Add(names.Build());
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature |
        X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.KeyCertSign, true));
    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
    {
        new("1.3.6.1.5.5.7.3.1")
    }, false));
    var now = DateTimeOffset.UtcNow;
    using var certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(3));
    using var privateFile = new FileStream(Path.Combine(directory, "server.key"), FileMode.CreateNew);
    using var privateWriter = new StreamWriter(privateFile);
    privateWriter.WriteLine(rsa.ExportPkcs8PrivateKeyPem());
    using var publicFile = new FileStream(Path.Combine(directory, "server.crt"), FileMode.CreateNew);
    using var publicWriter = new StreamWriter(publicFile);
    publicWriter.WriteLine(certificate.ExportCertificatePem());
}

static async Task InitializeAsync(int port, string certificate, string profile, string password, string pin)
{
    var options = new NpgsqlConnectionStringBuilder
    {
        Host = "127.0.0.1", Port = port, Database = "postgres", Username = "postgres", Password = password,
        SslMode = SslMode.VerifyFull, RootCertificate = certificate, Pooling = false
    };

    await using (var admin = new NpgsqlConnection(options.ConnectionString))
    {
        await admin.OpenAsync();
        var version = await admin.ExecuteScalarAsync<int>("SELECT current_setting('server_version_num')::integer;");
        if (version != 180007)
            throw new InvalidOperationException("Se requiere PostgreSQL 18.7.");
        await admin.ExecuteAsync("CREATE DATABASE frubber TEMPLATE template0 ENCODING 'UTF8';");
    }

    options.Database = "frubber";
    var factory = new NpgsqlConnectionFactory(new DatabaseOptions(options.ConnectionString));
    var installed = await new Postgres20BaselineInstaller().InstallAsync(factory, CancellationToken.None);
    if (!installed.IsSuccess || installed.Value != 1)
        throw new InvalidOperationException("No se instaló la línea base nueva.");

    await using var database = new NpgsqlConnection(options.ConnectionString);
    await database.OpenAsync();
    var setupAssembly = System.Reflection.Assembly.GetExecutingAssembly();
    await using (var transaction = await database.BeginTransactionAsync())
    {
        foreach (var name in setupAssembly.GetManifestResourceNames().Where(name => name.StartsWith("server.", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            using var script = new StreamReader(setupAssembly.GetManifestResourceStream(name)!);
            await database.ExecuteAsync(await script.ReadToEndAsync(), transaction: transaction);
        }
        var hash = new Pbkdf2PinHasher().HashPin(pin);
        await database.ExecuteAsync("""
            INSERT INTO public.app_user(display_name, role, is_active, pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
            VALUES ('Administrador', 'ADMIN', true, @Salt, @Hash, @Iterations, @AlgorithmVersion);
            """, hash, transaction);
        await transaction.CommitAsync();
    }

    var fingerprint = await new WindowsMachineCodeProvider().GetFingerprintAsync(CancellationToken.None);
    var registration = new Postgres20WorkstationRegistration(EcdsaLicenseSignatureVerifier.CreateProduction(),
        fingerprint.MachineCode, SystemClock.Instance);
    var registered = await registration.RegisterAsync(database, "Caja principal", fingerprint.MachineCode, CancellationToken.None);
    if (!registered.IsSuccess)
        throw new InvalidOperationException("No se pudo autorizar la caja principal.");

    options.Username = registered.Value.Role;
    options.Password = registered.Value.Password;
    var store = new Postgres20WorkstationStore(new DatabaseOptions(options.ConnectionString));
    var signedIn = await store.AuthenticateAsync(1, pin, CancellationToken.None);
    if (!signedIn.IsSuccess)
        throw new InvalidOperationException("No se pudo validar el PIN inicial.");

    var plain = JsonSerializer.SerializeToUtf8Bytes(new { options.ConnectionString, ServerMachineCode = fingerprint.MachineCode });
    try
    {
        var protectedBytes = ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("FrubberPOS.Workstation.v2"),
            DataProtectionScope.CurrentUser);
        await using var destination = new FileStream(profile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await destination.WriteAsync(protectedBytes);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(plain);
    }
}
