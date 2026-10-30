using FrubberPOS.Application.System;

namespace FrubberPOS.Infrastructure.System;

/// <summary>
/// Well-known Windows directories (JHO-22): everything lives under one
/// stable root so backup, support and uninstall behave predictably.
/// The PostgreSQL connection string comes from
/// <c>FRUBBERPOS_CONNECTION_STRING</c>; when missing, falls back to a
/// local development database so the app can still start offline.
/// </summary>
public sealed class WindowsAppPaths : IAppPaths
{
    public WindowsAppPaths()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "FrubberPOS"),
            Environment.GetEnvironmentVariable("FRUBBERPOS_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=frubberpos;Username=frubber;Password=frubber;")
    {
    }

    public WindowsAppPaths(string rootDirectory, string databaseConnectionString)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("El directorio raíz no puede estar vacío.", nameof(rootDirectory));
        }

        if (string.IsNullOrWhiteSpace(databaseConnectionString))
        {
            throw new ArgumentException("La cadena de conexión no puede estar vacía.", nameof(databaseConnectionString));
        }

        RootDirectory = rootDirectory;
        DatabaseConnectionString = databaseConnectionString;
        LogDirectory = Path.Combine(rootDirectory, "Logs");
    }

    public string RootDirectory { get; }

    public string DatabaseConnectionString { get; }

    public string LogDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
