using System.Text;
using System.Text.Json;
using FrubberPOS.DataMigration;
using FrubberPOS.Infrastructure.Licensing;

if (args is not [var source, var licenseFile, var reportPath])
{
    Console.Error.WriteLine("Uso en el servidor: FrubberPOS.DataMigration <respaldo.db> <licencia.frb1 o -> <informe.json>");
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
try
{
    Console.WriteLine("Cadena de conexión del administrador del destino (TLS VerifyFull):");
    var connection = ReadConnection();
    var fingerprint = await new WindowsMachineCodeProvider().GetFingerprintAsync(cancellation.Token);
    var signedLicense = licenseFile == "-" ? "" : await File.ReadAllTextAsync(licenseFile, cancellation.Token);

    // Reserve a new path before contacting the destination.
    await using var report = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    var useCase = new ImportSqliteUseCase(EcdsaLicenseSignatureVerifier.CreateProduction());
    var result = await useCase.ExecuteAsync(new(source, signedLicense, fingerprint.MachineCode, connection), cancellation.Token);
    if (result.IsFailure)
    {
        await JsonSerializer.SerializeAsync(report, new { Confirmed = false, Status = result.Error!.Message });
        Console.Error.WriteLine(result.Error.Message);
        return 1;
    }
    await JsonSerializer.SerializeAsync(report, result.Value, new JsonSerializerOptions { WriteIndented = true });
    await report.FlushAsync();
    Console.WriteLine("Migración confirmada. El informe contiene el cuadre de los datos importados.");
    if (result.Value.LegacyOpenCashSessions > 0)
        Console.WriteLine("El respaldo contiene una caja abierta asignada al puesto histórico. Para el corte operativo, cierre primero la caja en la versión 1.0 y use un respaldo nuevo.");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Se canceló la migración.");
    return 1;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine("Revise el respaldo, el archivo de licencia y el destino del informe. Si la operación alcanzó el destino, compruebe su estado antes de repetirla.");
    return 1;
}

static string ReadConnection()
{
    if (Console.IsInputRedirected)
        return Console.ReadLine() ?? "";
    var buffer = new StringBuilder();
    for (var key = Console.ReadKey(intercept: true); key.Key != ConsoleKey.Enter; key = Console.ReadKey(intercept: true))
    {
        if (key.Key == ConsoleKey.Backspace && buffer.Length > 0)
            buffer.Length--;
        else if (!char.IsControl(key.KeyChar))
            buffer.Append(key.KeyChar);
    }
    Console.WriteLine();
    return buffer.ToString();
}
