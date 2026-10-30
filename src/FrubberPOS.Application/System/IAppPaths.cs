namespace FrubberPOS.Application.System;

/// <summary>
/// Well-known local directories of this installation. Production root is
/// <c>%ProgramData%\FrubberPOS</c>; tests supply a temporary root.
/// </summary>
public interface IAppPaths
{
    string RootDirectory { get; }

    /// <summary>
    /// PostgreSQL connection string. Production reads
    /// <c>FRUBBERPOS_CONNECTION_STRING</c>; tests supply a temporary
    /// connection string pointing at a test database.
    /// </summary>
    string DatabaseConnectionString { get; }

    string LogDirectory { get; }

    /// <summary>
    /// Creates the directories safely (idempotent): called once at startup
    /// before the container builds.
    /// </summary>
    void EnsureCreated();
}
