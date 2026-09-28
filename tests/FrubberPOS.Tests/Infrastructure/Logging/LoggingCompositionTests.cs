using FrubberPOS.Application.Logging;
using FrubberPOS.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace FrubberPOS.Tests.Infrastructure.Logging;

/// <summary>
/// Validates that the production registrations compose into a valid
/// container. The canonical root is Desktop <c>ServiceConfiguration</c>,
/// which tests cannot reference (net10.0 vs net10.0-windows); keep the
/// registration below in sync with it.
/// </summary>
public sealed class LoggingCompositionTests
{
    [Fact]
    public void ProductionRegistrations_BuildValidContainerResolvingLogger()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N"));
        try
        {
            using (var provider = new ServiceCollection()
                .AddSingleton<IAppLogger>(_ => SerilogLoggerFactory.Create(new LoggingOptions(directory)))
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                }))
            {
                var logger = provider.GetRequiredService<IAppLogger>();
                logger.LogInformation("DI_SMOKE", "Contenedor válido.");
            }

            var file = Assert.Single(Directory.GetFiles(directory, "log*.txt"));
            Assert.Contains("DI_SMOKE", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
