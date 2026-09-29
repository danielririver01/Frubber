using FrubberPOS.Application.System;

namespace FrubberPOS.Infrastructure.System;

/// <summary>
/// Well-known Windows directories (JHO-22): everything lives under one
/// stable root so backup, support and uninstall behave predictably.
/// </summary>
public sealed class WindowsAppPaths : IAppPaths
{
    public WindowsAppPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FrubberPOS"))
    {
    }

    public WindowsAppPaths(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("El directorio raíz no puede estar vacío.", nameof(rootDirectory));
        }

        RootDirectory = rootDirectory;
        DatabaseFilePath = Path.Combine(rootDirectory, "frubberpos.db");
        LogDirectory = Path.Combine(rootDirectory, "Logs");
    }

    public string RootDirectory { get; }

    public string DatabaseFilePath { get; }

    public string LogDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
