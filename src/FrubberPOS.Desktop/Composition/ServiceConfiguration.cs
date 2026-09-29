using System.IO;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Logging;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Security;
using FrubberPOS.Application.System;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.Authentication;
using FrubberPOS.Infrastructure.Logging;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Users;
using FrubberPOS.Infrastructure.Security;
using FrubberPOS.Infrastructure.System;
using Microsoft.Extensions.DependencyInjection;

namespace FrubberPOS.Desktop.Composition;

/// <summary>
/// Sole Composition Root helper (P-DD-002, JHO-14). Only <see cref="App"/>
/// calls this. Use cases, ViewModels and presentation services register
/// here; Infrastructure is referenced for composition only.
/// No Service Locator: the built provider never leaves <see cref="App"/>.
/// </summary>
public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(IServiceCollection services, IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);

        services.AddSingleton(paths);
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IFileSystem, LocalFileSystem>();
        services.AddSingleton<IAppLogger>(_ => SerilogLoggerFactory.Create(new LoggingOptions(paths.LogDirectory)));
        services.AddSingleton<IDatabaseConnectionFactory>(provider =>
            new SqliteConnectionFactory(new DatabaseOptions(provider.GetRequiredService<IAppPaths>().DatabaseFilePath)));
        services.AddSingleton<IUnitOfWorkFactory, SqliteUnitOfWorkFactory>();
        services.AddSingleton<IPinHasher, Pbkdf2PinHasher>();
        services.AddSingleton<IUserRepository, DapperUserRepository>();
        services.AddSingleton<ICurrentSession, CurrentSession>();
        services.AddTransient<AuthenticateUserUseCase>();
        services.AddTransient<ListActiveUsersUseCase>();
        services.AddTransient<SignOutUseCase>();
        services.AddTransient<CreateUserUseCase>();
        services.AddTransient<UpdateUserUseCase>();
        services.AddTransient<ResetUserPinUseCase>();
        services.AddTransient<SetUserActiveStateUseCase>();
        services.AddTransient<DeleteUserUseCase>();
        services.AddTransient<ListUsersUseCase>();
        services.AddTransient<GetUserUseCase>();

        return services;
    }

    public static ServiceProvider BuildProvider(IAppPaths paths)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, paths);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    /// <summary>
    /// Production paths, created safely. Called once at startup (JHO-22).
    /// </summary>
    public static IAppPaths ProductionAppPaths()
    {
        IAppPaths paths = new WindowsAppPaths();
        paths.EnsureCreated();
        return paths;
    }
}
