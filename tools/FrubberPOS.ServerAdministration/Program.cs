using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Persistence.Workstations;
using FrubberPOS.Infrastructure.System;
using Npgsql;

if (args is not [var name, var code, var output])
{
    Console.Error.WriteLine("Uso en el servidor: FrubberPOS.ServerAdministration <nombre> <código-puesto> <archivo.cfg>");
    return 1;
}

Console.WriteLine("Cadena de conexión del administrador del servidor local (entrada oculta, TLS VerifyFull):");
var secret = new StringBuilder();
ConsoleKeyInfo key;
if (Console.IsInputRedirected)
{
    secret.Append(Console.ReadLine());
}
else
{
    while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && secret.Length > 0)
            secret.Length--;
        else if (!char.IsControl(key.KeyChar))
            secret.Append(key.KeyChar);
    }
}
Console.WriteLine();

FileStream? destination = null;
var saved = false;
try
{
    var options = new NpgsqlConnectionStringBuilder(secret.ToString());
    secret.Clear();
    if (options.Host is not ("127.0.0.1" or "localhost" or "::1") || options.SslMode != SslMode.VerifyFull)
    {
        Console.Error.WriteLine("Ejecute la autorización en el servidor con conexión local y TLS VerifyFull.");
        return 1;
    }
    options.PersistSecurityInfo = false;
    options.IncludeErrorDetail = false;

    // Reserve the destination before creating the role. Existing profiles are never overwritten.
    destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    var fingerprint = await new WindowsMachineCodeProvider().GetFingerprintAsync(CancellationToken.None);
    await using var connection = new NpgsqlConnection(options.ConnectionString);
    await connection.OpenAsync(CancellationToken.None);
    var service = new Postgres20WorkstationRegistration(EcdsaLicenseSignatureVerifier.CreateProduction(),
        fingerprint.MachineCode, SystemClock.Instance);
    var result = await service.RegisterAsync(connection, name, code, CancellationToken.None);
    if (result.IsFailure)
    {
        Console.Error.WriteLine(result.Error!.Message);
        return 1;
    }

    options.Username = result.Value.Role;
    options.Password = result.Value.Password;
    var plain = JsonSerializer.SerializeToUtf8Bytes(new
    {
        ConnectionString = options.ConnectionString,
        ServerMachineCode = fingerprint.MachineCode,
    });
    try
    {
        var protectedBytes = ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("FrubberPOS.Workstation.v2"),
            DataProtectionScope.CurrentUser);
        await destination.WriteAsync(protectedBytes);
        destination.Flush(flushToDisk: true);
        saved = true;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(plain);
    }
    Console.WriteLine($"Puesto autorizado. {result.Value.Active} de {result.Value.Seats} puestos ocupados.");
    Console.WriteLine("Perfil protegido para esta cuenta de Windows en este equipo.");
    return 0;
}
catch (Exception ex) when (ex is NpgsqlException or IOException or UnauthorizedAccessException or
                           ArgumentException or CryptographicException)
{
    Console.Error.WriteLine("No se pudo confirmar la autorización o guardar el perfil. Revise los puestos en el servidor antes de repetir el alta.");
    return 1;
}
finally
{
    if (destination is not null)
    {
        await destination.DisposeAsync();
        if (!saved)
            File.Delete(output);
    }
}
