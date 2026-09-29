using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Logging;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Security;
using FrubberPOS.Application.System;
using FrubberPOS.Application.Users;
using FrubberPOS.Infrastructure.Logging;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Users;
using FrubberPOS.Infrastructure.Security;
using FrubberPOS.Infrastructure.System;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;
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
    public async Task ProductionRegistrations_BuildValidContainerResolvingLogger()
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
                .AddSingleton<IDatabaseConnectionFactory>(sp =>
                    new SqliteConnectionFactory(new DatabaseOptions(sp.GetRequiredService<IAppPaths>().DatabaseFilePath)))
                .AddSingleton<IUnitOfWorkFactory, SqliteUnitOfWorkFactory>()
                .AddSingleton<IPinHasher, Pbkdf2PinHasher>()
                .AddSingleton<IUserRepository, DapperUserRepository>()
                .AddSingleton<ICurrentSession, FakeCurrentSession>()
                .AddTransient<AuthenticateUserUseCase>()
                .AddTransient<ListActiveUsersUseCase>()
                .AddTransient<SignOutUseCase>()
                .AddTransient<CreateUserUseCase>()
                .AddTransient<UpdateUserUseCase>()
                .AddTransient<ResetUserPinUseCase>()
                .AddTransient<SetUserActiveStateUseCase>()
                .AddTransient<DeleteUserUseCase>()
                .AddTransient<ListUsersUseCase>()
                .AddTransient<GetUserUseCase>()
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                }))
            {
                Assert.NotNull(provider.GetRequiredService<IAppPaths>());
                Assert.NotNull(provider.GetRequiredService<IClock>());
                Assert.NotNull(provider.GetRequiredService<IFileSystem>());
                Assert.NotNull(provider.GetRequiredService<IDatabaseConnectionFactory>());
                Assert.NotNull(provider.GetRequiredService<IPinHasher>());
                Assert.NotNull(provider.GetRequiredService<IUserRepository>());
                Assert.NotNull(provider.GetRequiredService<ICurrentSession>());
                Assert.NotNull(provider.GetRequiredService<AuthenticateUserUseCase>());
                Assert.NotNull(provider.GetRequiredService<CreateUserUseCase>());
                Assert.NotNull(provider.GetRequiredService<UpdateUserUseCase>());
                Assert.NotNull(provider.GetRequiredService<ResetUserPinUseCase>());
                Assert.NotNull(provider.GetRequiredService<SetUserActiveStateUseCase>());
                Assert.NotNull(provider.GetRequiredService<DeleteUserUseCase>());
                Assert.NotNull(provider.GetRequiredService<ListUsersUseCase>());
                Assert.NotNull(provider.GetRequiredService<GetUserUseCase>());

                var units = provider.GetRequiredService<IUnitOfWorkFactory>();
                await using (var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None))
                {
                    Assert.NotNull(unit.Connection);
                }

                var logger = provider.GetRequiredService<IAppLogger>();
                logger.LogInformation("DI_SMOKE", "Contenedor válido.");
            }

            var file = Assert.Single(Directory.GetFiles(paths.LogDirectory, "log*.txt"));
            Assert.Contains("DI_SMOKE", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
