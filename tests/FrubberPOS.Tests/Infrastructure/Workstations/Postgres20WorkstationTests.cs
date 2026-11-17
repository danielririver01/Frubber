using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Dapper;
using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Cash;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Products;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Users;
using FrubberPOS.Application.Workstations;
using FrubberPOS.Desktop.Authentication;
using FrubberPOS.Desktop.Common;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Desktop.Views;
using FrubberPOS.Domain.Products;
using FrubberPOS.Domain.Users;
using FrubberPOS.Domain.Licensing;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Workstations;
using FrubberPOS.Infrastructure.Security;
using FrubberPOS.Infrastructure.System;
using FrubberPOS.Tests.TestDoubles;
using FrubberPOS.Tests.Desktop;
using Npgsql;

namespace FrubberPOS.Tests.Infrastructure.Workstations;

public sealed class Postgres20WorkstationTests
{
    private const string AdminVariable = "FRUBBER_JHO136_WORKSTATION_ADMIN";
    private static readonly string Scripts = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "database", "postgresql"));

    [IsolatedWorkstationFact(recoveryOnly: true)]
    public async Task RecoversExperimentalSaleAfterLostResponseAndNewWorkstationModel()
    {
        // Run separately with a fresh cluster, as the security script creates cluster-wide roles.
        var options = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(AdminVariable)!);
        Assert.True(options.Host == "127.0.0.1" && options.Database == "frubber" &&
                    options.Username == "postgres" && options.SslMode == SslMode.VerifyFull && options.Port != 5432);
        await using var admin = new NpgsqlConnection(options.ConnectionString);
        await admin.OpenAsync();
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>("""
            SELECT count(*) FROM pg_roles WHERE rolname = 'frubber_puesto' OR rolname LIKE 'puesto_%';
            """));
        var cancellationToken = CancellationToken.None;
        var installation = await new Postgres20BaselineInstaller().InstallAsync(
            new NpgsqlConnectionFactory(new DatabaseOptions(options.ConnectionString)), cancellationToken);
        Assert.True(installation.IsSuccess, installation.Error?.Message);
        await using (var setup = await admin.BeginTransactionAsync())
        {
            foreach (var script in new[] { "002_Security.sql", "003_RegistroVenta.sql", "004_CobroIdempotente.sql",
                         "005_AperturaCaja.sql", "006_LicenciaPuesto.sql", "007_CierreCaja.sql", "008_AdministracionPuesto.sql", "010_RecuperacionAutenticada.sql" })
            {
                await admin.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(Scripts, script)), transaction: setup);
            }
            await setup.CommitAsync();
        }

        var role = await RegisterAsync(admin, options, "Puesto de recuperación");
        var userId = await SeedUserAsync(admin, "Cajero de recuperación", "2374");
        var otherUser = await SeedUserAsync(admin, "Otro cajero", "6182");
        await admin.ExecuteAsync("""
            INSERT INTO public.product(plu, name, unit, price_cop, stock_milli, is_active, unit_locked)
            VALUES ('P1', 'Manzana', 'KG', 1500, 10000, true, true);
            """);

        var directory = Directory.CreateTempSubdirectory("frubber-recovery-");
        var journalPath = Path.Combine(directory.FullName, "sale-attempt");
        try
        {
            var firstStore = new Postgres20WorkstationStore(new(role.ConnectionString), experimentalRecovery: true);
            Assert.Equal(ErrorCode.Authorization, (await new Postgres20WorkstationStore(new(role.ConnectionString))
                .CheckAsync(cancellationToken)).Error?.Code);
            var checkedTerminal = await firstStore.CheckAsync(cancellationToken);
            Assert.True(checkedTerminal.IsSuccess, checkedTerminal.Error?.Message);
            var terminal = checkedTerminal.Value;
            var databaseId = (await firstStore.ReadRecoveryDatabaseIdAsync(cancellationToken)).Value;
            WorkstationSale? written = null;
            var first = CreateModel(firstStore, async (attempt, token) =>
            {
                var result = await firstStore.SendExperimentalSaleAttemptAsync(attempt, token);
                Assert.True(result.IsSuccess, result.Error?.Message);
                written = result.Value;
                return Result<WorkstationSale>.Failure(new Error(ErrorCode.Connection, "Respuesta de prueba descartada"));
            });
            await first.SignInAsync("2374");
            await first.OpenCashAsync();
            first.Plu = "P1";
            first.Received = "1500";
            await first.AddProductAsync();
            await first.CheckoutAsync();
            Assert.NotNull(written);
            Assert.True(first.ShowRecovery);
            Assert.False(first.CanCloseCash);
            var originalAttempt = new ProtectedSaleAttemptJournal(journalPath).Read()!.Attempt;

            var restartedStore = new Postgres20WorkstationStore(new(role.ConnectionString), experimentalRecovery: true);
            Assert.Equal(ErrorCode.Authorization, (await restartedStore.SendExperimentalSaleAttemptAsync(
                originalAttempt, cancellationToken)).Error?.Code);
            Assert.True((await restartedStore.AuthenticateAsync(otherUser, "6182", cancellationToken)).IsSuccess);
            Assert.Equal(ErrorCode.Authorization, (await restartedStore.SendExperimentalSaleAttemptAsync(
                originalAttempt, cancellationToken)).Error?.Code);
            var restarted = CreateModel(restartedStore, async (attempt, token) =>
            {
                Assert.Equal(originalAttempt.RequestId, attempt.RequestId);
                Assert.Equal(originalAttempt.CashId, attempt.CashId);
                var result = await restartedStore.SendExperimentalSaleAttemptAsync(attempt, token);
                Assert.Equal(written, result.Value);
                return result;
            });
            await restarted.SignInAsync("2374");
            Assert.True(restarted.ShowRecovery);
            await restarted.RecoverSaleAsync();
            Assert.True(restarted.CanAcknowledgeReceipt, restarted.Message);
            Assert.Contains("V-000001", restarted.Message);
            await restarted.AcknowledgeReceiptAsync();
            Assert.True(restarted.ShowSale);
            Assert.Equal(1, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale;"));
            Assert.Equal(9000L, await admin.ExecuteScalarAsync<long>("SELECT stock_milli FROM public.product;"));
            Assert.Equal(1L, await admin.ExecuteScalarAsync<long>("SELECT last_number FROM public.ticket_counter;"));

            await admin.ExecuteAsync("""
                CREATE FUNCTION public.frubber_test_drop_sale_connection() RETURNS trigger
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $$
                BEGIN
                    PERFORM pg_terminate_backend(pg_backend_pid());
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER frubber_test_drop_sale_connection BEFORE INSERT ON public.sale_line
                FOR EACH ROW EXECUTE FUNCTION public.frubber_test_drop_sale_connection();
                """);
            var interrupted = CreateModel(restartedStore, restartedStore.SendExperimentalSaleAttemptAsync);
            await interrupted.SignInAsync("2374");
            interrupted.Plu = "P1";
            interrupted.Received = "1500";
            await interrupted.AddProductAsync();
            await interrupted.CheckoutAsync();
            Assert.True(interrupted.ShowRecovery);
            Assert.False(interrupted.CanAcknowledgeReceipt);
            Assert.Equal(1, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale;"));
            Assert.Equal(9000L, await admin.ExecuteScalarAsync<long>("SELECT stock_milli FROM public.product;"));
            await admin.ExecuteAsync("DROP TRIGGER frubber_test_drop_sale_connection ON public.sale_line;");

            var recoveredStore = new Postgres20WorkstationStore(new(role.ConnectionString), experimentalRecovery: true);
            var recovered = CreateModel(recoveredStore, recoveredStore.SendExperimentalSaleAttemptAsync);
            await recovered.SignInAsync("2374");
            Assert.True(recovered.ShowRecovery);
            await recovered.RecoverSaleAsync();
            Assert.True(recovered.CanAcknowledgeReceipt, recovered.Message);
            Assert.Contains("V-000002", recovered.Message);
            await recovered.AcknowledgeReceiptAsync();
            Assert.Equal(2, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale;"));
            Assert.Equal(8000L, await admin.ExecuteScalarAsync<long>("SELECT stock_milli FROM public.product;"));
            Assert.Equal(2L, await admin.ExecuteScalarAsync<long>("SELECT last_number FROM public.ticket_counter;"));

            if (Environment.GetEnvironmentVariable("FRUBBER_JHO114_SIGNED_LICENSE_PATH") is { Length: > 0 } licensePath)
            {
                await VerifyExecutableRecoveryAsync(admin, role, terminal.Id, directory.FullName, licensePath);
            }

            WorkstationViewModel CreateModel(Postgres20WorkstationStore store,
                Func<SaleAttempt, CancellationToken, Task<Result<WorkstationSale>>> send)
            {
                var session = new CurrentSession();
                var gate = new FakeOperationalGate();
                return new WorkstationViewModel(terminal, [new(userId, "Cajero", UserRole.Cashier)], store,
                    new(store, session), new(store, session, gate), new(store, session, gate),
                    new(store, session, gate), new WorkstationSaleRecovery(
                        new ProtectedSaleAttemptJournal(journalPath), databaseId, terminal.Id, session, gate, send));
            }
        }
        finally
        {
            foreach (var file in directory.EnumerateFiles())
            {
                file.Delete();
            }
            directory.Delete();
        }
    }

    [IsolatedWorkstationFact]
    public async Task TwoWorkstations_OpenSellAndCloseIndependently()
    {
        var adminOptions = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(AdminVariable)!);
        Assert.True(adminOptions.Host == "127.0.0.1" && adminOptions.Database == "frubber" &&
                    adminOptions.Username == "postgres" && adminOptions.SslMode == SslMode.VerifyFull &&
                    adminOptions.Port != 5432, "The test requires an isolated IPv4 loopback cluster on a non-default port.");
        await using var admin = new NpgsqlConnection(adminOptions.ConnectionString);
        await admin.OpenAsync();
        Assert.Equal("on", await admin.ExecuteScalarAsync<string>("SHOW ssl;"));
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>("""
            SELECT count(*) FROM pg_catalog.pg_roles
            WHERE rolname IN ('frubber_puesto', 'frubber_puesto_test') OR rolname LIKE 'puesto_%';
            """));
        var install = await new Postgres20BaselineInstaller().InstallAsync(
            new NpgsqlConnectionFactory(new DatabaseOptions(adminOptions.ConnectionString)), CancellationToken.None);
        Assert.True(install.IsSuccess, install.Error?.Message);
        Assert.Equal(1, install.Value);
        await using (var setup = await admin.BeginTransactionAsync())
        {
            foreach (var script in new[] { "002_Security.sql", "003_RegistroVenta.sql", "005_AperturaCaja.sql", "006_LicenciaPuesto.sql", "007_CierreCaja.sql", "008_AdministracionPuesto.sql", "009_ProductosUsuariosPuesto.sql" })
            {
                await admin.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(Scripts, script)), transaction: setup);
            }
            await setup.CommitAsync();
        }

        var firstOptions = await RegisterAsync(admin, adminOptions, "Caja de prueba A");
        var secondOptions = await RegisterAsync(admin, adminOptions, "Caja de prueba B");
        var first = new Postgres20WorkstationStore(new DatabaseOptions(firstOptions.ConnectionString));
        var second = new Postgres20WorkstationStore(new DatabaseOptions(secondOptions.ConnectionString));
        var token = CancellationToken.None;
        var firstSession = new CurrentSession();
        var secondSession = new CurrentSession();
        var firstGate = new FakeOperationalGate();
        var secondGate = new FakeOperationalGate();
        var firstStartup = new CheckWorkstationStartupUseCase(first, firstGate);
        var firstTerminal = await firstStartup.ExecuteAsync(new(), token);
        var secondTerminal = await new CheckWorkstationStartupUseCase(second, secondGate).ExecuteAsync(new(), token);
        Assert.True(firstTerminal.IsSuccess, firstTerminal.Error?.Message);
        Assert.True(secondTerminal.IsSuccess, secondTerminal.Error?.Message);
        Assert.NotEqual(firstTerminal.Value.Id, secondTerminal.Value.Id);
        await VerifySignedSeatLimitAsync(admin, first, second);
        Assert.Equal(1, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.schema_migration;"));
        Assert.Null(await admin.ExecuteScalarAsync<string>("SELECT to_regclass('public.schemamigration')::text;"));
        var badHost = new NpgsqlConnectionStringBuilder(firstOptions.ConnectionString) { Host = "localhost" };
        Assert.Equal(ErrorCode.Connection,
            (await new Postgres20WorkstationStore(new DatabaseOptions(badHost.ConnectionString)).CheckAsync(token)).Error?.Code);
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var certificateRequest = new CertificateRequest("CN=Certificado ajeno a Frubber", key, HashAlgorithmName.SHA256);
            using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var wrongRootPath = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(wrongRootPath, certificate.ExportCertificatePem(), token);
                var wrongRoot = new NpgsqlConnectionStringBuilder(firstOptions.ConnectionString) { RootCertificate = wrongRootPath };
                Assert.Equal(ErrorCode.Connection,
                    (await new Postgres20WorkstationStore(new DatabaseOptions(wrongRoot.ConnectionString)).CheckAsync(token)).Error?.Code);
            }
            finally
            {
                File.Delete(wrongRootPath);
            }
        }
        var withoutTls = new NpgsqlConnectionStringBuilder(firstOptions.ConnectionString) { SslMode = SslMode.Disable };
        await using (var deniedConnection = new NpgsqlConnection(withoutTls.ConnectionString))
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() => deniedConnection.OpenAsync(token));
            Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, denied.SqlState);
        }

        var firstUser = await SeedUserAsync(admin, "Cajero A", "1234");
        var secondUser = await SeedUserAsync(admin, "Cajero B", "2345");
        var firstLogin = new AuthenticateWorkstationUserUseCase(first, firstSession);
        var firstOpen = new OpenWorkstationCashUseCase(first, firstSession, firstGate);
        var secondOpen = new OpenWorkstationCashUseCase(second, secondSession, secondGate);
        Assert.Equal(ErrorCode.Authorization, (await firstOpen.ExecuteAsync(new(1000), token)).Error?.Code);
        Assert.Equal(ErrorCode.Authentication, (await firstLogin.ExecuteAsync(new(firstUser, "4321"), token)).Error?.Code);
        Assert.Null(firstSession.Current);
        var loginOne = await firstLogin.ExecuteAsync(new(firstUser, "1234"), token);
        var loginTwo = await new AuthenticateWorkstationUserUseCase(second, secondSession)
            .ExecuteAsync(new(secondUser, "2345"), token);
        Assert.True(loginOne.IsSuccess, loginOne.Error?.Message);
        Assert.True(loginTwo.IsSuccess, loginTwo.Error?.Message);
        Assert.Equal(3, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.pin_attempt_log;"));
        firstGate.Blocked = new Error(ErrorCode.LicenseBlocked, "Licencia de prueba bloqueada.");
        Assert.Equal(ErrorCode.LicenseBlocked, (await firstStartup.ExecuteAsync(new(), token)).Error?.Code);
        Assert.Equal(ErrorCode.LicenseBlocked, (await firstOpen.ExecuteAsync(new(1000), token)).Error?.Code);
        firstGate.Blocked = null;

        var openings = await Task.WhenAll(
            firstOpen.ExecuteAsync(new(1000), token), firstOpen.ExecuteAsync(new(1000), token),
            secondOpen.ExecuteAsync(new(2000), token));
        var firstCash = Assert.Single(openings.Take(2), result => result.IsSuccess).Value;
        Assert.Equal(ErrorCode.Conflict, Assert.Single(openings.Take(2), result => result.IsFailure).Error?.Code);
        Assert.True(openings[2].IsSuccess, openings[2].Error?.Message);
        var secondCash = openings[2].Value;
        Assert.NotEqual(firstCash, secondCash);
        Assert.Equal(firstCash, (await first.GetCashAsync(token)).Value.CashId);
        Assert.Equal(secondCash, (await second.GetCashAsync(token)).Value.CashId);
        Assert.Equal(2, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.cash_session WHERE closed_at IS NULL;"));

        await using (var roleConnection = new NpgsqlConnection(firstOptions.ConnectionString))
        {
            await roleConnection.OpenAsync();
            Assert.True(await roleConnection.ExecuteScalarAsync<bool>(
                "SELECT ssl FROM pg_catalog.pg_stat_ssl WHERE pid = pg_backend_pid();"));
            await AssertDeniedAsync(roleConnection, "INSERT INTO public.cash_session DEFAULT VALUES;");
            await AssertDeniedAsync(roleConnection, "INSERT INTO public.sale DEFAULT VALUES;");
            await AssertDeniedAsync(roleConnection, "SELECT pin_hash FROM public.app_user;");
            await AssertDeniedAsync(roleConnection, "SELECT last_number FROM public.ticket_counter;");
            await AssertDeniedAsync(roleConnection, "SELECT version FROM public.schema_migration;");
            await AssertDeniedAsync(roleConnection, "SELECT signed_payload FROM public.license_state;");
            var crossSale = await Assert.ThrowsAsync<PostgresException>(() => roleConnection.ExecuteAsync("""
                SELECT * FROM public.app_registrar_venta(
                    @CashId, @UserId, 0, 'TRANSFER', NULL, @RequestId, '[]'::jsonb);
                """, new { CashId = secondCash, UserId = (long)firstUser, RequestId = Guid.NewGuid() }));
            Assert.Equal(PostgresErrorCodes.CheckViolation, crossSale.SqlState);
            Assert.Contains("caja", crossSale.MessageText, StringComparison.OrdinalIgnoreCase);
        }

        var productId = await admin.ExecuteScalarAsync<int>("""
            INSERT INTO public.product(plu, name, unit, price_cop, stock_milli, is_active, unit_locked)
            VALUES ('P1', 'Manzana de prueba', 'KG', 1000, 100000, true, true)
            RETURNING product_id::int;
            """);
        var request = new FinalizeSaleRequest(
            [new(productId, "P1", "Manzana de prueba", ProductUnit.Kg, 1000, 1000)], 0, "CASH", 1000);
        var firstCheckout = new FinalizeWorkstationSaleUseCase(first, firstSession, firstGate);
        var secondCheckout = new FinalizeWorkstationSaleUseCase(second, secondSession, secondGate);
        firstGate.Blocked = new Error(ErrorCode.LicenseBlocked, "Licencia de prueba bloqueada.");
        Assert.Equal(ErrorCode.LicenseBlocked, (await firstCheckout.ExecuteAsync(request, token)).Error?.Code);
        firstGate.Blocked = null;
        await admin.ExecuteAsync("""
            CREATE FUNCTION public.frubber_test_fail_sale_line() RETURNS trigger
            LANGUAGE plpgsql AS $$ BEGIN
                RAISE EXCEPTION 'Fallo de prueba después de asignar el número' USING ERRCODE = '23514';
            END $$;
            CREATE TRIGGER frubber_test_fail_sale_line BEFORE INSERT ON public.sale_line
            FOR EACH ROW EXECUTE FUNCTION public.frubber_test_fail_sale_line();
            """);
        Assert.Equal(ErrorCode.Conflict, (await firstCheckout.ExecuteAsync(request, token)).Error?.Code);
        Assert.Equal(0L, await admin.ExecuteScalarAsync<long>("SELECT last_number FROM public.ticket_counter;"));
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale;"));
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale_line;"));
        Assert.Equal(100000L, await admin.ExecuteScalarAsync<long>("SELECT stock_milli FROM public.product;"));
        await admin.ExecuteAsync("DROP TRIGGER frubber_test_fail_sale_line ON public.sale_line;");

        await Task.WhenAll(SellAsync(firstCheckout), SellAsync(secondCheckout));
        Assert.Equal(Enumerable.Range(1, 40).Select(i => (long)i),
            await admin.QueryAsync<long>("SELECT sale_number FROM public.sale ORDER BY sale_number;"));
        Assert.Equal(20, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.sale WHERE cash_session_id = @Cash AND cashier_user_id = @UserId;",
            new { Cash = firstCash, UserId = firstUser }));
        Assert.Equal(20, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.sale WHERE cash_session_id = @Cash AND cashier_user_id = @UserId;",
            new { Cash = secondCash, UserId = secondUser }));
        Assert.Equal(40L, await admin.ExecuteScalarAsync<long>("SELECT last_number FROM public.ticket_counter;"));
        Assert.Equal(40, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale_line;"));
        Assert.Equal(60000L, await admin.ExecuteScalarAsync<long>("SELECT stock_milli FROM public.product;"));

        var historyUnits = new NpgsqlUnitOfWorkFactory(new NpgsqlConnectionFactory(new DatabaseOptions(firstOptions.ConnectionString)));
        var historyRepository = new Postgres20SaleHistorySearchRepository();
        var history = new SearchSalesHistoryUseCase(historyRepository, historyUnits, firstSession);
        Assert.Equal(ErrorCode.Authorization, (await history.ExecuteAsync(new(null, null, 1), token)).Error?.Code);
        await using (var historyUnit = await historyUnits.CreateAsync(UnitOfWorkMode.ReadOnly, token))
        {
            var firstPage = await historyRepository.SearchAsync(historyUnit, 0, long.MaxValue, 0, 10, token);
            Assert.Equal(40, firstPage.TotalCount);
            Assert.Equal(10, firstPage.Items.Count);
            Assert.All(firstPage.Items, item => Assert.Equal("ACTIVE", item.Status));
            var emptyPage = await historyRepository.SearchAsync(historyUnit, 0, long.MaxValue, 40, 10, token);
            Assert.Equal(40, emptyPage.TotalCount);
            Assert.Empty(emptyPage.Items);
        }

        var adminUser = await SeedUserAsync(admin, "Administrador de prueba", "8541", "ADMIN");
        var adminStore = new Postgres20WorkstationStore(new DatabaseOptions(firstOptions.ConnectionString));
        var adminSession = new CurrentSession();
        var detail = new GetSaleDetailUseCase(new Postgres20SaleHistorySearchRepository(adminStore), historyUnits, adminSession);
        Assert.Equal(ErrorCode.Authorization, (await detail.ExecuteAsync(new(1), token)).Error?.Code);
        var adminLogin = new AuthenticateWorkstationUserUseCase(adminStore, adminSession);
        Assert.True((await adminLogin.ExecuteAsync(new(adminUser, "8541"), token)).IsSuccess);
        var saleDetail = await detail.ExecuteAsync(new(1), token);
        Assert.True(saleDetail.IsSuccess, saleDetail.Error?.Message);
        Assert.Single(saleDetail.Value.Lines);
        Assert.Equal(ErrorCode.NotFound, (await detail.ExecuteAsync(new(999), token)).Error?.Code);
        var adminUnits = new NpgsqlUnitOfWorkFactory(
            new NpgsqlConnectionFactory(new DatabaseOptions(firstOptions.ConnectionString)), serializeWorkstationAdmin: true);
        var products = new Postgres20ProductRepository(adminStore);
        var accounts = new Postgres20UserRepository(adminStore);
        var hasher = new Pbkdf2PinHasher();
        var productSearch = new SearchProductsUseCase(products, adminUnits, adminSession);
        var createProduct = new CreateProductUseCase(products, adminUnits, adminSession);
        var created = await createProduct.ExecuteAsync(new("P2", "Pera de prueba", ProductUnit.Kg, 2000, 0), token);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var pearId = await admin.ExecuteScalarAsync<int>("SELECT product_id::integer FROM public.product WHERE plu = 'P2';");
        Assert.True((await new UpdateProductUseCase(products, adminUnits, adminSession)
            .ExecuteAsync(new(pearId, "P2", "Pera madura", ProductUnit.Kg, 2500), token)).IsSuccess);
        Assert.True((await new AdjustStockUseCase(products, adminUnits, adminSession)
            .ExecuteAsync(new(pearId, 2000), token)).IsSuccess);
        Assert.True((await new SetProductActiveStateUseCase(products, adminUnits, adminSession)
            .ExecuteAsync(new(pearId, false), token)).IsSuccess);
        Assert.True((await productSearch.ExecuteAsync(new("madura", ProductStatusFilter.Inactivos), token)).IsSuccess);
        var deleteProduct = new DeleteProductUseCase(products, adminUnits, adminSession);
        Assert.True((await deleteProduct.ExecuteAsync(new(pearId), token)).IsSuccess);
        Assert.Equal(ErrorCode.Conflict, (await deleteProduct.ExecuteAsync(new(productId), token)).Error?.Code);
        Assert.Equal(ErrorCode.Authorization, (await new CreateProductUseCase(
            new Postgres20ProductRepository(first), adminUnits, firstSession)
            .ExecuteAsync(new("P3", "Bloqueado", ProductUnit.Kg, 1000, 0), token)).Error?.Code);

        var createUser = new CreateUserUseCase(accounts, hasher, adminUnits, adminSession);
        Assert.True((await createUser.ExecuteAsync(new("Cajero adicional", UserRole.Cashier, "3278"), token)).IsSuccess);
        var addedUser = await admin.ExecuteScalarAsync<int>(
            "SELECT user_id::integer FROM public.app_user WHERE display_name = 'Cajero adicional';");
        var updateUser = new UpdateUserUseCase(accounts, adminUnits, adminSession);
        Assert.True((await updateUser.ExecuteAsync(new(addedUser, "Cajero editado", UserRole.Cashier), token)).IsSuccess);
        Assert.Equal(ErrorCode.Conflict,
            (await updateUser.ExecuteAsync(new(adminUser, "Administrador de prueba", UserRole.Cashier), token)).Error?.Code);
        Assert.True((await new ResetUserPinUseCase(accounts, hasher, adminUnits, adminSession)
            .ExecuteAsync(new(addedUser, "9376"), token)).IsSuccess);
        Assert.True((await new AuthenticateWorkstationUserUseCase(
            new Postgres20WorkstationStore(new DatabaseOptions(secondOptions.ConnectionString)), new CurrentSession())
            .ExecuteAsync(new(addedUser, "9376"), token)).IsSuccess);
        var deleteUser = new DeleteUserUseCase(accounts, adminUnits, adminSession);
        Assert.Equal(ErrorCode.Conflict, (await deleteUser.ExecuteAsync(new(addedUser), token)).Error?.Code);
        Assert.True((await new SetUserActiveStateUseCase(accounts, adminUnits, adminSession)
            .ExecuteAsync(new(addedUser, false), token)).IsSuccess);
        Assert.True((await createUser.ExecuteAsync(new("Cajero sin historial", UserRole.Cashier, "9753"), token)).IsSuccess);
        var unusedUser = await admin.ExecuteScalarAsync<int>(
            "SELECT user_id::integer FROM public.app_user WHERE display_name = 'Cajero sin historial';");
        Assert.True((await deleteUser.ExecuteAsync(new(unusedUser), token)).IsSuccess);
        Assert.Equal(ErrorCode.Authorization, (await new ListUsersUseCase(
            new Postgres20UserRepository(first), adminUnits, firstSession).ExecuteAsync(new(), token)).Error?.Code);
        StaViewHarness.Run(() =>
        {
            StaViewHarness.EnsureApplicationWithTheme();
            var navigator = new FakeNavigator();
            var productPage = new ProductsViewModel(productSearch,
                new SetProductActiveStateUseCase(products, adminUnits, adminSession), deleteProduct, navigator);
            var userPage = new UsersViewModel(new ListUsersUseCase(accounts, adminUnits, adminSession),
                new SetUserActiveStateUseCase(accounts, adminUnits, adminSession), deleteUser, navigator);
            var historyPage = new SalesHistoryViewModel(new SearchSalesHistoryUseCase(
                new Postgres20SaleHistorySearchRepository(adminStore), historyUnits, adminSession), navigator);
            var detailPage = new SaleDetailViewModel(detail);
            Assert.Same(productPage, new ProductsView(productPage).DataContext);
            Assert.Same(userPage, new UsersView(userPage).DataContext);
            Assert.Same(historyPage, new SalesHistoryView(historyPage).DataContext);
            var detailDialog = new SaleDetailDialog(detailPage, 1, viewerIsAdmin: true);
            StaViewHarness.Await(productPage.SearchAsync());
            StaViewHarness.Await(userPage.LoadAsync());
            StaViewHarness.Await(historyPage.InitializeAsync());
            StaViewHarness.Await(detailPage.LoadAsync(1, viewerIsAdmin: true));
            Assert.Single(productPage.Products);
            Assert.Contains(userPage.Users, row => row.Source.UserId == adminUser);
            Assert.Equal(40, historyPage.Items.Count);
            Assert.False(detailPage.CanVoid);
            detailDialog.Close();

            var directory = new[]
            {
                new ActiveUserDto(firstUser, "Cajero A", UserRole.Cashier),
                new ActiveUserDto(adminUser, "Administrador de prueba", UserRole.Admin)
            };
            var screen = new WorkstationViewModel(firstTerminal.Value, directory, adminStore, adminLogin,
                new OpenWorkstationCashUseCase(adminStore, adminSession, firstGate),
                new CloseWorkstationCashUseCase(adminStore, adminSession, firstGate),
                new FinalizeWorkstationSaleUseCase(adminStore, adminSession, firstGate));
            var window = new WorkstationWindow(screen, () => historyPage, () => productPage, () => userPage);
            StaViewHarness.PumpLoaded(window);
            Assert.Equal(System.Windows.Visibility.Collapsed, window.AdminMenu.Visibility);
            screen.SelectedUser = directory[1];
            StaViewHarness.Await(screen.SignInAsync("8541"));
            StaViewHarness.PumpLoaded(window);
            Assert.Equal(System.Windows.Visibility.Visible, window.AdminMenu.Visibility);
            screen.SelectedUser = directory[0];
            StaViewHarness.Await(screen.SignInAsync("1234"));
            StaViewHarness.PumpLoaded(window);
            Assert.Equal(System.Windows.Visibility.Collapsed, window.AdminMenu.Visibility);
            window.Close();
        });
        Assert.True((await adminLogin.ExecuteAsync(new(adminUser, "8541"), token)).IsSuccess);
        var activeAdminToken = Assert.IsType<Guid>(adminStore.AdminToken);
        await using (var adminRole = new NpgsqlConnection(firstOptions.ConnectionString))
        {
            var lastAdmin = await Assert.ThrowsAsync<PostgresException>(() => adminRole.ExecuteAsync(
                "SELECT public.app_admin_user_set_active(@Token, @UserId, false);",
                new { Token = activeAdminToken, UserId = (long)adminUser }));
            Assert.Equal(PostgresErrorCodes.CheckViolation, lastAdmin.SqlState);
        }
        await using (var otherRole = new NpgsqlConnection(secondOptions.ConnectionString))
        {
            await otherRole.OpenAsync(token);
            await AssertDeniedAsync(otherRole, "SELECT * FROM public.app_admin_sale_lines(NULL, 1);");
            await AssertDeniedAsync(otherRole, "SELECT * FROM public.sale_line;");
            await AssertDeniedAsync(otherRole, "SELECT * FROM public.workstation_admin_session;");
            await AssertDeniedAsync(otherRole, "SELECT * FROM public.app_admin_users(NULL);");
            await AssertDeniedAsync(otherRole, "SELECT public.app_admin_product_set_stock(NULL, 1, 0);");
            await AssertDeniedAsync(otherRole, "SELECT public.app_admin_user_set_active(NULL, 1, false);");
            await AssertDeniedAsync(otherRole, "UPDATE public.product SET stock_milli = 0;");
            await AssertDeniedAsync(otherRole, "UPDATE public.app_user SET role = 'ADMIN';");
            var crossTerminalToken = await Assert.ThrowsAsync<PostgresException>(() => otherRole.ExecuteAsync(
                "SELECT * FROM public.app_admin_users(@Token);", new { Token = activeAdminToken }));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, crossTerminalToken.SqlState);
        }
        await admin.ExecuteAsync("UPDATE public.app_user SET is_active = false WHERE user_id = @UserId;", new { UserId = adminUser });
        Assert.Equal(ErrorCode.Authorization, (await detail.ExecuteAsync(new(1), token)).Error?.Code);

        var firstClose = new CloseWorkstationCashUseCase(first, firstSession, firstGate);
        var secondClose = new CloseWorkstationCashUseCase(second, secondSession, secondGate);
        Assert.Equal(ErrorCode.Authorization, (await firstClose.ExecuteAsync(new(secondCash), token)).Error?.Code);
        var users = (await first.ListUsersAsync(token)).Value;
        var screenA = new WorkstationViewModel(firstTerminal.Value, users, first, firstLogin, firstOpen, firstClose, firstCheckout)
            { SelectedUser = users.Single(user => user.UserId == firstUser) };
        var screenB = new WorkstationViewModel(secondTerminal.Value, users, second,
            new AuthenticateWorkstationUserUseCase(second, secondSession), secondOpen, secondClose, secondCheckout)
            { SelectedUser = users.Single(user => user.UserId == secondUser) };
        await screenA.SignInAsync("1234");
        await screenB.SignInAsync("2345");
        Assert.True(screenA.CanCloseCash, screenA.Message);
        Assert.True(screenB.CanCloseCash, screenB.Message);
        var closeA = screenA.CloseCashAsync();
        var transferB = secondCheckout.ExecuteAsync(
            request with { PaymentMethod = "TRANSFER", ReceivedCop = null, DiscountCop = 100 }, token);
        await Task.WhenAll(closeA, transferB);
        Assert.True(screenA.ShowCash, screenA.Message);
        Assert.Contains($"Efectivo esperado {MoneyText.FormatCop(21000)}", screenA.Message);
        Assert.True(screenB.ShowSale);
        var firstSummary = await firstClose.ExecuteAsync(new(firstCash), token);
        Assert.True(firstSummary.IsSuccess, firstSummary.Error?.Message);
        Assert.True((await transferB).IsSuccess, (await transferB).Error?.Message);
        Assert.Equal(new WorkstationCashClosure(firstCash, 1000, 20000, 0, 0, 20000, 21000, 20), firstSummary.Value);
        Assert.Null((await first.GetCashAsync(token)).Value.CashId);
        Assert.Equal(secondCash, (await second.GetCashAsync(token)).Value.CashId);
        Assert.Equal(secondCash, await admin.ExecuteScalarAsync<long>(
            "SELECT cash_session_id FROM public.cash_session WHERE closed_at IS NULL;"));
        var saleAfterClose = await secondCheckout.ExecuteAsync(request, token);
        Assert.True(saleAfterClose.IsSuccess, saleAfterClose.Error?.Message);
        await screenB.CloseCashAsync();
        Assert.True(screenB.ShowCash, screenB.Message);
        Assert.Contains($"Transferencias {MoneyText.FormatCop(900)}", screenB.Message);
        Assert.Contains($"Efectivo esperado {MoneyText.FormatCop(23000)}", screenB.Message);
        var secondSummary = await secondClose.ExecuteAsync(new(secondCash), token);
        Assert.True(secondSummary.IsSuccess, secondSummary.Error?.Message);
        Assert.Equal(new WorkstationCashClosure(secondCash, 2000, 21000, 900, 100, 21900, 23000, 22), secondSummary.Value);

        var reopened = await firstOpen.ExecuteAsync(new(3000), token);
        Assert.True(reopened.IsSuccess, reopened.Error?.Message);
        var repeated = await firstClose.ExecuteAsync(new(firstCash), token);
        Assert.True(repeated.IsSuccess, repeated.Error?.Message);
        Assert.Equal(firstSummary.Value, repeated.Value);
        Assert.Equal(reopened.Value, await admin.ExecuteScalarAsync<long>(
            "SELECT cash_session_id FROM public.cash_session WHERE closed_at IS NULL;"));

        async Task SellAsync(FinalizeWorkstationSaleUseCase checkout)
        {
            for (var sale = 0; sale < 20; sale++)
            {
                var result = await checkout.ExecuteAsync(request, token);
                Assert.True(result.IsSuccess, result.Error?.Message);
                Assert.Equal(1000L, result.Value.TotalCop);
            }
        }
    }

    private static async Task VerifySignedSeatLimitAsync(
        NpgsqlConnection admin, Postgres20WorkstationStore first, Postgres20WorkstationStore second)
    {
        const string machine = "AB12CD34EF567890";
        const string keyId = "lab-key";
        var date = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new EcdsaLicenseSignatureVerifier(key.ExportSubjectPublicKeyInfo(), keyId);
        var firstGate = new Postgres20LicenseGate(first, verifier, machine, SystemClock.Instance);
        var secondGate = new Postgres20LicenseGate(second, verifier, machine, SystemClock.Instance);
        var token = CancellationToken.None;

        Assert.Equal(ErrorCode.LicenseBlocked, (await firstGate.CheckAsync(token))?.Code);
        await StoreAsync(Sign($"FRB1|{keyId}|{machine}|{date}"));
        Assert.Null(await firstGate.CheckAsync(token));
        Assert.Equal(ErrorCode.LicenseBlocked, (await secondGate.CheckAsync(token))?.Code);

        var validTwoSeats = Sign($"FRB2|{keyId}|{machine}|{date}|2");
        await StoreAsync(validTwoSeats.Replace("|2.", "|3.", StringComparison.Ordinal));
        Assert.Equal(ErrorCode.LicenseBlocked, (await firstGate.CheckAsync(token))?.Code);
        await StoreAsync(validTwoSeats);
        Assert.Null(await firstGate.CheckAsync(token));
        Assert.Null(await secondGate.CheckAsync(token));

        var wrongServer = new Postgres20LicenseGate(second, verifier, "0000111122223333", SystemClock.Instance);
        Assert.Equal(ErrorCode.LicenseBlocked, (await wrongServer.CheckAsync(token))?.Code);

        string Sign(string payload) => payload + "." + Convert.ToBase64String(key.SignData(
            Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        Task StoreAsync(string payload) => admin.ExecuteAsync("""
            INSERT INTO public.license_state(license_state_id,payload_version,signed_payload)
            VALUES (1,2,@Payload)
            ON CONFLICT (license_state_id) DO UPDATE SET signed_payload=excluded.signed_payload;
            """, new { Payload = payload });
    }

    private static async Task AssertDeniedAsync(NpgsqlConnection role, string sql)
    {
        var denied = await Assert.ThrowsAsync<PostgresException>(() => role.ExecuteAsync(sql));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
    }

    private static async Task VerifyExecutableRecoveryAsync(NpgsqlConnection admin, NpgsqlConnectionStringBuilder role,
        long terminalId, string directory, string licensePath)
    {
        var signed = (await File.ReadAllTextAsync(licensePath)).Trim();
        Assert.True(WorkstationLicenseFormat.TryParse(signed, out var license), "Se requiere licencia firmada de laboratorio.");
        Assert.True(EcdsaLicenseSignatureVerifier.CreateProduction().VerifyCanonical(
            license!.Key.KeyId, license.CanonicalPayload, license.Key.Signature));
        await admin.ExecuteAsync("""
            INSERT INTO public.license_state (license_state_id, payload_version, signed_payload)
            VALUES (1, @version, @signed);
            """, new { signed, version = license.CanonicalPayload.StartsWith("FRB1|", StringComparison.Ordinal) ? 1 : 2 });
        var profile = Path.Combine(directory, "recovery.cfg");
        await WriteProfileAsync(role);

        var journalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrubberPOS", "RecuperacionLab", $"puesto-{terminalId}.dpapi");
        Assert.False(File.Exists(journalPath));
        Assert.False(File.Exists(journalPath + ".lock"));
        var app = Path.GetFullPath(Path.Combine(Scripts, "..", "..", "src", "FrubberPOS.Desktop", "bin",
            "Release", "net10.0-windows", "FrubberPOS.Desktop.exe"));
        Assert.True(File.Exists(app), "Compile Release antes de ejecutar el ensayo del instalable.");

        await admin.ExecuteAsync("""
            CREATE TRIGGER frubber_test_drop_sale_connection BEFORE INSERT ON public.sale_line
            FOR EACH ROW EXECUTE FUNCTION public.frubber_test_drop_sale_connection();
            """);
        try
        {
            using (var first = Start())
            {
                try
                {
                    var window = await FindAsync(AutomationElement.RootElement, "WorkstationWindow", first.Id);
                    await SignInAsync(window);
                    await SubmitSaleAsync(window);
                    await FindAsync(window, "RecoverPendingSale");
                    Assert.Equal(2, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale;"));
                }
                finally
                {
                    await CloseAsync(first);
                }
            }
            await admin.ExecuteAsync("DROP TRIGGER frubber_test_drop_sale_connection ON public.sale_line;");
            using var restarted = Start();
            try
            {
                var window = await FindAsync(AutomationElement.RootElement, "WorkstationWindow", restarted.Id);
                await SignInAsync(window);
                Invoke(await FindAsync(window, "RecoverPendingSale"));
                var acknowledge = await FindAsync(window, "AcknowledgeRecoveredSale", requireEnabled: true);
                Assert.Contains("V-000003", (await FindAsync(window, "Result")).Current.Name);
                Invoke(acknowledge);
                await FindAsync(window, "Checkout");
                Assert.Equal(3, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale;"));
                Assert.Equal(7000L, await admin.ExecuteScalarAsync<long>("SELECT stock_milli FROM public.product;"));
                Assert.Equal(3L, await admin.ExecuteScalarAsync<long>("SELECT last_number FROM public.ticket_counter;"));
            }
            finally
            {
                await CloseAsync(restarted);
            }

            await admin.ExecuteAsync("""
                CREATE FUNCTION public.frubber_test_wait_sale() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(114, 991);
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER frubber_test_wait_sale BEFORE INSERT ON public.sale_line
                FOR EACH ROW EXECUTE FUNCTION public.frubber_test_wait_sale();
                SELECT pg_advisory_lock(114, 991);
                """);
            await using var relay = new RecoveryLoopbackRelay(role.Port);
            var forwarded = new NpgsqlConnectionStringBuilder(role.ConnectionString) { Port = relay.Port };
            await WriteProfileAsync(forwarded);
            using (var first = Start())
            {
                try
                {
                    var window = await FindAsync(AutomationElement.RootElement, "WorkstationWindow", first.Id);
                    await SignInAsync(window);
                    await SubmitSaleAsync(window);
                    await WaitForDatabaseAsync("""
                        SELECT EXISTS (
                            SELECT FROM pg_stat_activity WHERE usename = @username AND wait_event = 'advisory'
                        );
                        """);
                    relay.HoldReplies();
                    Assert.True(await admin.ExecuteScalarAsync<bool>("SELECT pg_advisory_unlock(114, 991);"));
                    await WaitForDatabaseAsync("SELECT count(*) = 4 FROM public.sale;");
                    relay.Cut();
                    await FindAsync(window, "RecoverPendingSale", requireEnabled: true);
                    Assert.Null(new ProtectedSaleAttemptJournal(journalPath).Read()?.Receipt);
                }
                finally
                {
                    await admin.ExecuteAsync("SELECT pg_advisory_unlock(114, 991);");
                    relay.Cut();
                    await CloseAsync(first);
                }
            }

            await WriteProfileAsync(role);
            using var afterCommit = Start();
            try
            {
                var window = await FindAsync(AutomationElement.RootElement, "WorkstationWindow", afterCommit.Id);
                await SignInAsync(window);
                Invoke(await FindAsync(window, "RecoverPendingSale"));
                var acknowledge = await FindAsync(window, "AcknowledgeRecoveredSale", requireEnabled: true);
                Assert.Contains("V-000004", (await FindAsync(window, "Result")).Current.Name);
                Invoke(acknowledge);
                await FindAsync(window, "Checkout");
                Assert.Equal(4, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale;"));
                Assert.Equal(6000L, await admin.ExecuteScalarAsync<long>("SELECT stock_milli FROM public.product;"));
                Assert.Equal(4L, await admin.ExecuteScalarAsync<long>("SELECT last_number FROM public.ticket_counter;"));
            }
            finally
            {
                await CloseAsync(afterCommit);
            }
        }
        finally
        {
            File.Delete(journalPath);
            File.Delete(journalPath + ".lock");
        }

        var second = await RegisterAsync(admin, role, "Puesto licenciado 2");
        var third = await RegisterAsync(admin, role, "Puesto licenciado 3");
        Assert.Equal(license.Seats == 1 ? ErrorCode.LicenseBlocked : (ErrorCode?)null,
            (await CheckSeatAsync(second))?.Code);
        Assert.Equal(ErrorCode.LicenseBlocked, (await CheckSeatAsync(third))?.Code);

        Task<Error?> CheckSeatAsync(NpgsqlConnectionStringBuilder options) =>
            new Postgres20LicenseGate(new(new DatabaseOptions(options.ConnectionString), experimentalRecovery: true),
                EcdsaLicenseSignatureVerifier.CreateProduction(), license.Key.MachineCode, SystemClock.Instance)
            .CheckAsync(CancellationToken.None);

        Process Start()
        {
            var info = new ProcessStartInfo(app) { UseShellExecute = false };
            info.ArgumentList.Add("--puesto-recuperacion-lab");
            info.ArgumentList.Add(profile);
            return Process.Start(info)!;
        }

        async Task WriteProfileAsync(NpgsqlConnectionStringBuilder options)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { options.ConnectionString, ServerMachineCode = license.Key.MachineCode });
            try
            {
                await File.WriteAllBytesAsync(profile, ProtectedData.Protect(bytes,
                    Encoding.UTF8.GetBytes("FrubberPOS.Workstation.v2"), DataProtectionScope.CurrentUser));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        async Task WaitForDatabaseAsync(string sql)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await admin.ExecuteScalarAsync<bool>(new CommandDefinition(sql,
                       new { username = role.Username }, cancellationToken: timeout.Token)))
            {
                await Task.Delay(50, timeout.Token);
            }
        }

        static async Task SubmitSaleAsync(AutomationElement window)
        {
            SetText(await FindAsync(window, "Plu"), "P1");
            SetText(await FindAsync(window, "Received"), "1500");
            Invoke(await FindAsync(window, "AddProduct"));
            Invoke(await FindAsync(window, "Checkout", requireEnabled: true));
        }

        static async Task SignInAsync(AutomationElement window)
        {
            SetText(await FindAsync(window, "Pin"), "2374");
            Invoke(await FindAsync(window, "SignIn"));
        }

        static void SetText(AutomationElement element, string text) =>
            ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);

        static void Invoke(AutomationElement element) =>
            ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

        static async Task<AutomationElement> FindAsync(AutomationElement root, string id, int? processId = null, bool requireEnabled = false)
        {
            Condition condition = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            if (processId is { } pid)
            {
                condition = new AndCondition(condition, new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!timeout.IsCancellationRequested)
            {
                var element = root.FindFirst(processId.HasValue ? TreeScope.Children : TreeScope.Descendants, condition);
                if (element is not null && (!requireEnabled || element.Current.IsEnabled))
                {
                    return element;
                }
                await Task.Delay(100);
            }
            throw new InvalidOperationException($"No apareció el control {id} en el ejecutable de laboratorio.");
        }

        static async Task CloseAsync(Process process)
        {
            if (process.HasExited)
            {
                return;
            }
            process.CloseMainWindow();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                await process.WaitForExitAsync();
                throw;
            }
        }
    }

    private static Task<int> SeedUserAsync(NpgsqlConnection admin, string name, string pin, string role = "CASHIER")
    {
        var hash = new Pbkdf2PinHasher().HashPin(pin);
        return admin.ExecuteScalarAsync<int>("""
            INSERT INTO public.app_user
                (display_name, role, is_active, pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
            VALUES (@Name, @Role, true, @Salt, @Hash, @Iterations, @AlgorithmVersion)
            RETURNING user_id::int;
            """, new { Name = name, Role = role, hash.Salt, hash.Hash, hash.Iterations, hash.AlgorithmVersion });
    }

    private static async Task<NpgsqlConnectionStringBuilder> RegisterAsync(
        NpgsqlConnection admin, NpgsqlConnectionStringBuilder options, string name)
    {
        var credentials = await admin.QuerySingleAsync<Credentials>("""
            SELECT db_role AS Role, generated_password AS Password
            FROM public.register_terminal(@Name, CAST(@Code AS char(16)));
            """, new { Name = name, Code = Guid.NewGuid().ToString("N")[..16] });
        return new NpgsqlConnectionStringBuilder(options.ConnectionString)
        {
            Username = credentials.Role,
            Password = credentials.Password,
        };
    }

    private sealed record Credentials(string Role, string Password);

    private sealed class IsolatedWorkstationFactAttribute : FactAttribute
    {
        public IsolatedWorkstationFactAttribute(bool recoveryOnly = false)
        {
            if (Environment.GetEnvironmentVariable("FRUBBER_JHO136_ISOLATED") != "1" ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AdminVariable)) ||
                recoveryOnly != (Environment.GetEnvironmentVariable("FRUBBER_JHO114_RECOVERY_ONLY") == "1"))
            {
                Skip = "Requires an explicitly enabled, disposable PostgreSQL cluster with TLS.";
            }
        }
    }
}
