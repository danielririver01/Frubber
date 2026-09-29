using FrubberPOS.Application.Logging;
using FrubberPOS.Application.System;
using FrubberPOS.Infrastructure.Logging;
using FrubberPOS.Infrastructure.System;
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
        var root = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N"));
        try
        {
            IAppPaths paths = new WindowsAppPaths(root);
            paths.EnsureCreated();

            using (var provider = new ServiceCollection()
                .AddSingleton(paths)
                .AddSingleton<IClock>(SystemClock.Instance)
                .AddSingleton<IFileSystem, LocalFileSystem>()
                .AddSingleton<IAppLogger>(_ => SerilogLoggerFactory.Create(new LoggingOptions(paths.LogDirectory)))
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                }))
            {
                Assert.NotNull(provider.GetRequiredService<IAppPaths>());
                Assert.NotNull(provider.GetRequiredService<IClock>());
                Assert.NotNull(provider.GetRequiredService<IFileSystem>());

                var logger = provider.GetRequiredService<IAppLogger>();
                logger.LogInformation("DI_SMOKE", "Contenedor válido.");
            }

            var file = Assert.Single(Directory.GetFiles(paths.LogDirectory, "log*.txt"));
            Assert.Contains("DI_SMOKE", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
