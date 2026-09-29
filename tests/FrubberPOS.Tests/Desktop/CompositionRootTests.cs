using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Logging;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Security;
using FrubberPOS.Application.Startup;
using FrubberPOS.Application.System;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.Composition;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.System;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// Exercises the real <c>ServiceConfiguration</c> with temporary paths:
/// no mirror to keep in sync (JHO-29).
/// </summary>
public sealed class CompositionRootTests
{
    [Fact]
    public async Task ProductionRegistrations_BuildValidContainer()
    {
        var root = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N"));
        var dbPath = string.Empty;
        try
        {
            IAppPaths paths = new WindowsAppPaths(root);
            paths.EnsureCreated();

            using var provider = ServiceConfiguration.BuildProvider(paths, new FakeNavigator());
            dbPath = provider.GetRequiredService<DatabaseOptions>().DatabaseFilePath;

            Assert.NotNull(provider.GetRequiredService<IAppPaths>());
            Assert.NotNull(provider.GetRequiredService<IClock>());
            Assert.NotNull(provider.GetRequiredService<IFileSystem>());
            Assert.NotNull(provider.GetRequiredService<IDatabaseConnectionFactory>());
            Assert.NotNull(provider.GetRequiredService<IUnitOfWorkFactory>());
            Assert.NotNull(provider.GetRequiredService<IPinHasher>());
            Assert.NotNull(provider.GetRequiredService<IUserRepository>());
            Assert.NotNull(provider.GetRequiredService<ICurrentSession>());
            Assert.NotNull(provider.GetRequiredService<AuthenticateUserUseCase>());
            Assert.NotNull(provider.GetRequiredService<ListActiveUsersUseCase>());
            Assert.NotNull(provider.GetRequiredService<SignOutUseCase>());
            Assert.NotNull(provider.GetRequiredService<CreateUserUseCase>());
            Assert.NotNull(provider.GetRequiredService<UpdateUserUseCase>());
            Assert.NotNull(provider.GetRequiredService<ResetUserPinUseCase>());
            Assert.NotNull(provider.GetRequiredService<SetUserActiveStateUseCase>());
            Assert.NotNull(provider.GetRequiredService<DeleteUserUseCase>());
            Assert.NotNull(provider.GetRequiredService<ListUsersUseCase>());
            Assert.NotNull(provider.GetRequiredService<GetUserUseCase>());
            Assert.NotNull(provider.GetRequiredService<CreateFirstAdministratorUseCase>());
            Assert.NotNull(provider.GetRequiredService<CheckFirstStartUseCase>());
            Assert.NotNull(provider.GetRequiredService<LoginViewModel>());
            Assert.NotNull(provider.GetRequiredService<FirstStartViewModel>());
            Assert.NotNull(provider.GetRequiredService<DatabaseOptions>());
            Assert.NotNull(provider.GetRequiredService<MigrationRunner>());

            var units = provider.GetRequiredService<IUnitOfWorkFactory>();
            await using (var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, CancellationToken.None))
            {
                Assert.NotNull(unit.Connection);
            }

            var logger = provider.GetRequiredService<IAppLogger>();
            logger.LogInformation("DI_SMOKE", "Contenedor válido.");
            provider.Dispose();

            var file = Assert.Single(Directory.GetFiles(paths.LogDirectory, "log*.txt"));
            Assert.Contains("DI_SMOKE", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={dbPath}"));
            Directory.Delete(root, recursive: true);
        }
    }
}
