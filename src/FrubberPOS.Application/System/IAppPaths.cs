namespace FrubberPOS.Application.System;

/// <summary>
/// Well-known local directories of this installation. Production root is
/// <c>%ProgramData%\FrubberPOS</c>; tests supply a temporary root.
/// </summary>
public interface IAppPaths
{
    string RootDirectory { get; }

    string DatabaseFilePath { get; }

    string LogDirectory { get; }

    /// <summary>
    /// Creates the directories safely (idempotent): called once at startup
    /// before the container builds.
    /// </summary>
    void EnsureCreated();
}
