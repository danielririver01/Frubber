using System.IO;
using FrubberPOS.Application.Logging;
using FrubberPOS.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace FrubberPOS.Desktop.Composition;

/// <summary>
/// Sole Composition Root helper (P-DD-002, JHO-14). Only <see cref="App"/>
/// calls this. Future use cases, ViewModels and presentation services
/// register here; Infrastructure is referenced for composition only.
/// No Service Locator: the built provider never leaves <see cref="App"/>.
/// </summary>
public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(IServiceCollection services, LoggingOptions loggingOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(loggingOptions);

        services.AddSingleton<IAppLogger>(_ => SerilogLoggerFactory.Create(loggingOptions));

        return services;
    }

    public static ServiceProvider BuildProvider(LoggingOptions loggingOptions)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, loggingOptions);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    /// <summary>
    /// Production logging directory. Resolving well-known local paths
    /// moves to JHO-22; until then the Composition Root owns this line.
    /// </summary>
    public static LoggingOptions ProductionLoggingOptions() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FrubberPOS",
            "Logs"));
}
