using System.IO;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.Logging;
using FrubberPOS.Application.Navigation;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Security;
using FrubberPOS.Application.Startup;
using FrubberPOS.Application.System;
using FrubberPOS.Application.Trial;
using FrubberPOS.Application.Users;
using FrubberPOS.Desktop.Authentication;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Logging;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Cash;
using FrubberPOS.Infrastructure.Persistence.Licensing;
using FrubberPOS.Infrastructure.Persistence.Products;
using FrubberPOS.Infrastructure.Persistence.Trial;
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
        services.AddSingleton(sp => new DatabaseOptions(sp.GetRequiredService<IAppPaths>().DatabaseFilePath));
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<IUnitOfWorkFactory, SqliteUnitOfWorkFactory>();
        services.AddSingleton<IPinHasher, Pbkdf2PinHasher>();
        services.AddSingleton<IUserRepository, DapperUserRepository>();
        services.AddSingleton<IMachineCodeProvider, WindowsMachineCodeProvider>();
        services.AddSingleton<ILicenseSignatureVerifier>(_ => EcdsaLicenseSignatureVerifier.CreateProduction());
        services.AddSingleton<ILicenseProtector, DpapiLicenseProtector>();
        services.AddSingleton<ILicenseRepository, DapperLicenseRepository>();
        services.AddSingleton<ITrialRepository, DapperTrialRepository>();
        services.AddSingleton<IProductRepository, DapperProductRepository>();
        services.AddSingleton<ICashSessionRepository, DapperCashSessionRepository>();
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
        services.AddTransient<CreateFirstAdministratorUseCase>();
        services.AddTransient<CheckFirstStartUseCase>();
        services.AddTransient<GetMachineCodeUseCase>();
        services.AddTransient<ActivateLicenseUseCase>();
        services.AddTransient<GetLicenseStatusUseCase>();
        services.AddTransient<EvaluateTrialUseCase>();
        services.AddTransient<CreateProductUseCase>();
        services.AddTransient<UpdateProductUseCase>();
        services.AddTransient<GetProductUseCase>();
        services.AddTransient<SearchProductsUseCase>();
        services.AddTransient<SetProductActiveStateUseCase>();
        services.AddTransient<DeleteProductUseCase>();
        services.AddTransient<AdjustStockUseCase>();
        services.AddTransient<OpenCashSessionUseCase>();
        services.AddTransient<GetOpenCashSessionUseCase>();
        services.AddTransient<CloseCashSessionUseCase>();
        services.AddTransient<GetCashSessionSummaryUseCase>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<FirstStartViewModel>();
        services.AddTransient<LicenseActivationViewModel>();
        services.AddTransient<TrialBlockedViewModel>();
        services.AddTransient<AdjustStockViewModel>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<ProductEditorViewModel>();
        services.AddTransient<CashViewModel>();

        return services;
    }

    public static ServiceProvider BuildProvider(IAppPaths paths, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        var services = new ServiceCollection();
        ConfigureServices(services, paths);
        services.AddSingleton(navigation);
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
