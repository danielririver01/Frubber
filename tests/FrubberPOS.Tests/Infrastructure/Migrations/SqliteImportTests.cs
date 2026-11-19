using System.Security.Cryptography;
using System.Text;
using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.DataMigration;
using FrubberPOS.Infrastructure.Licensing;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace FrubberPOS.Tests.Infrastructure.Migrations;

public sealed class SqliteImportTests
{
    [ImportFact]
    public async Task ImportsClosedCashHistoryAndPreservesSourceAndEveryBusinessValue()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var before = SHA256.HashData(await File.ReadAllBytesAsync(fixture.Source));
        var result = await fixture.ImportAsync();
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.All(result.Value.Tables, check => Assert.True(check.ValuesMatch, check.Table));
        Assert.True(result.Value.LicenseVerified);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(fixture.Source)));
        await using var target = await fixture.OpenTargetAsync();
        Assert.Equal(2, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.app_user;"));
        Assert.Equal(1, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product;"));
        Assert.Equal(12345L, await target.ExecuteScalarAsync<long>("SELECT stock_milli FROM public.product WHERE product_id=17;"));
        Assert.Equal(2100L, await target.ExecuteScalarAsync<long>("SELECT price_cop FROM public.product WHERE product_id=17;"));
        Assert.Equal(1000L, await target.ExecuteScalarAsync<long>("SELECT total_cop FROM public.sale WHERE sale_id=31;"));
        Assert.Equal(50L, await target.ExecuteScalarAsync<long>("SELECT discount_cop FROM public.sale WHERE sale_id=31;"));
        Assert.Equal(-300, await target.ExecuteScalarAsync<int>("SELECT legacy_sold_at_offset_minutes FROM public.sale WHERE sale_id=31;"));
        Assert.Equal(500L, await target.ExecuteScalarAsync<long>("SELECT quantity_milli FROM public.sale_line WHERE sale_id=31;"));
        Assert.Equal(7L, await target.ExecuteScalarAsync<long>("SELECT last_number FROM public.ticket_counter WHERE scope='venta';"));
        Assert.Equal(1, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.cash_session c JOIN public.terminal t USING(terminal_id) WHERE t.machine_code='0000000000000000' AND c.closed_at IS NOT NULL;"));
        Assert.Equal(3000L, await target.ExecuteScalarAsync<long>("SELECT closed_expected_cash_cop FROM public.cash_session WHERE cash_session_id=21;"));
        Assert.False(await target.ExecuteScalarAsync<bool>("SELECT is_active FROM public.app_user WHERE user_id=2;"));
        Assert.Equal("CASHIER", await target.ExecuteScalarAsync<string>("SELECT role FROM public.app_user WHERE user_id=2;"));
        Assert.Equal(9600, await target.ExecuteScalarAsync<int>("SELECT baud_rate FROM public.scale_setting;"));
        Assert.Equal(1600000000000L, await target.ExecuteScalarAsync<long>("SELECT (extract(epoch FROM first_run_at)*1000)::bigint FROM public.trial_state;"));
        Assert.Equal(18L, await target.ExecuteScalarAsync<long>("INSERT INTO public.product(plu,name,unit,price_cop,stock_milli,is_active,unit_locked) VALUES ('NEXT','Siguiente','KG',100,0,true,false) RETURNING product_id;"));
    }

    [ImportFact]
    public async Task RejectsImportWhenPersistedStockDiffersEvenIfWritesSucceeded()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        await using var target = await fixture.OpenTargetAsync();
        await target.ExecuteAsync("""
            CREATE FUNCTION public.change_import_stock() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN NEW.stock_milli := NEW.stock_milli + 1; RETURN NEW; END $$;
            CREATE TRIGGER change_import_stock BEFORE INSERT ON public.product
            FOR EACH ROW EXECUTE FUNCTION public.change_import_stock();
            """);
        var result = await fixture.ImportAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("cuadre", result.Error!.Message);
        Assert.Equal(0, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.app_user;"));
        Assert.Equal(0, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product;"));
        Assert.Equal(0, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.license_state;"));
        Assert.Equal(0L, await target.ExecuteScalarAsync<long>("SELECT last_number FROM public.ticket_counter WHERE scope='venta';"));
    }

    [ImportFact]
    public async Task FailingTargetWriteRollsBackPreviouslyCopiedRowsAndCanRetry()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        await using var target = await fixture.OpenTargetAsync();
        await target.ExecuteAsync("""
            CREATE FUNCTION public.reject_import_line() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test import failure'; END $$;
            CREATE TRIGGER reject_import_line BEFORE INSERT ON public.sale_line FOR EACH ROW EXECUTE FUNCTION public.reject_import_line();
            """);
        var result = await fixture.ImportAsync();
        Assert.True(result.IsFailure);
        Assert.Equal(0, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product;"));
        Assert.Equal(0, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.app_user;"));
        Assert.Equal(0, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale;"));
        Assert.Equal(0, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.cash_session;"));
        Assert.Equal(0L, await target.ExecuteScalarAsync<long>("SELECT last_number FROM public.ticket_counter WHERE scope='venta';"));
        await target.ExecuteAsync("DROP TRIGGER reject_import_line ON public.sale_line; DROP FUNCTION public.reject_import_line();");
        Assert.True((await fixture.ImportAsync()).IsSuccess);
    }

    [ImportFact]
    public async Task RejectsNonemptyDestinationAndInvalidLicenseWithoutChangingIt()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var invalid = await fixture.ImportAsync(fixture.License + "invalid");
        Assert.True(invalid.IsFailure);
        await using var target = await fixture.OpenTargetAsync();
        Assert.Equal(0, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.app_user;"));
        await target.ExecuteAsync("INSERT INTO public.product(plu,name,unit,price_cop,stock_milli,is_active,unit_locked) VALUES ('EXISTING','Existente','KG',100,0,true,false);");
        var result = await fixture.ImportAsync();
        Assert.True(result.IsFailure);
        Assert.Equal("EXISTING", await target.ExecuteScalarAsync<string>("SELECT plu FROM public.product;"));
        Assert.Equal(0, await target.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sale;"));
    }

    private sealed class ImportFactAttribute : FactAttribute
    {
        public ImportFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("FRUBBER_JHO124_ISOLATED") != "1" ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FRUBBER_JHO124_ADMIN")))
                Skip = "Requires an explicitly enabled isolated PostgreSQL cluster.";
        }
    }

    private sealed class ImportFixture : IAsyncDisposable
    {
        private readonly string adminConnection;
        private readonly string database;
        private readonly DirectoryInfo directory;
        private readonly ECDsa key;
        private const string Machine = "A123456789ABCDEF";
        public string Source { get; }
        public string License { get; }
        private readonly EcdsaLicenseSignatureVerifier verifier;
        private string TargetConnection => new NpgsqlConnectionStringBuilder(adminConnection) { Database = database }.ConnectionString;

        private ImportFixture(string adminConnection)
        {
            this.adminConnection = adminConnection;
            database = "import_" + Guid.NewGuid().ToString("N");
            directory = Directory.CreateTempSubdirectory("frubber-import-");
            Source = Path.Combine(directory.FullName, "source.db");
            key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            verifier = new(key.ExportSubjectPublicKeyInfo(), "test");
            const string canonical = "FRB1|test|" + Machine + "|20260101";
            License = canonical + "." + Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(canonical),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }

        public static async Task<ImportFixture> CreateAsync()
        {
            var options = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FRUBBER_JHO124_ADMIN")!);
            Assert.Equal("127.0.0.1", options.Host);
            Assert.NotEqual(5432, options.Port);
            var fixture = new ImportFixture(options.ConnectionString);
            await using var admin = new NpgsqlConnection(options.ConnectionString);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE {fixture.database};");
            var baseline = await new Postgres20BaselineInstaller().InstallAsync(new NpgsqlConnectionFactory(
                new DatabaseOptions(fixture.TargetConnection)), CancellationToken.None);
            Assert.True(baseline.IsSuccess, baseline.Error?.Message);
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fixture.Source, Pooling = false }.ConnectionString);
            await source.OpenAsync();
            var schema = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","database","sqlite10-reference","2026-11-18","schema.sql"));
            await source.ExecuteAsync(await File.ReadAllTextAsync(schema));
            var salt = new byte[16];
            var hash = new byte[32];
            var seal = new DpapiLicenseProtector().Protect(Encoding.UTF8.GetBytes("FRB1|test|" + Machine + "|20260101"));
            await source.ExecuteAsync("""
                INSERT INTO "User" VALUES (1,'Administrador','ADMIN',1,@Salt,@Hash,10000,1);
                INSERT INTO "User" VALUES (2,'Cajero histórico','CASHIER',1,@Salt,@Hash,10000,1);
                INSERT INTO Product VALUES(17,'P17','Tomate','KG',2100,12345,1,1);
                INSERT INTO CashSession(CashSessionId,OpenedAtUtcMs,OpenedAtOffsetMinutes,OpenedByUserId,OpenedByNameSnapshot,OpeningBalanceCop)
                VALUES (21,1700000000000,-300,2,'Cajero histórico',2000);
                INSERT INTO Sale VALUES(31,7,21,2,'Cajero histórico',1700000001000,-300,1050,50,1000,'CASH',2000,1000);
                INSERT INTO SaleLine VALUES(41,31,1,17,'P17','Tomate','KG',2100,500,1050);
                UPDATE CashSession SET ClosedAtUtcMs=1700000002000,ClosedAtOffsetMinutes=-300,ClosedByUserId=1,
                    ClosedByNameSnapshot='Administrador',ClosedCashSalesCop=1000,ClosedTransferSalesCop=0,
                    ClosedDiscountCop=50,ClosedNetSalesCop=1000,ClosedExpectedCashCop=3000,ClosedSaleCount=1 WHERE CashSessionId=21;
                UPDATE "User" SET IsActive=0 WHERE UserId=2;
                INSERT INTO SystemSequence VALUES('SaleNumber',7);
                INSERT INTO BusinessSetting(BusinessSettingId,BusinessName) VALUES(1,'Fixture');
                INSERT INTO TrialState VALUES(1,1600000000000,1700000002000);
                INSERT INTO ScaleSetting(ScaleSettingId) VALUES(1);
                INSERT INTO LicenseState VALUES(1,1,@Seal,1700000002000);
                INSERT INTO SchemaMigration VALUES(1,'001_Initial.sql',@Checksum,1700000000000);
                INSERT INTO SchemaMigration VALUES(2,'002_Trial.sql',@Checksum,1700000000000);
                INSERT INTO SchemaMigration VALUES(3,'003_OpeningBalanceCorrection.sql',@Checksum,1700000000000);
                INSERT INTO SchemaMigration VALUES(4,'004_Scale.sql',@Checksum,1700000000000);
                """, new { Salt = salt, Hash = hash, Seal = seal, Checksum = new string('A',64) });
            return fixture;
        }

        public Task<FrubberPOS.Application.Common.Result<MigrationReport>> ImportAsync(string? license = null) =>
            new ImportSqliteUseCase(verifier).ExecuteAsync(new(Source, license ?? License, Machine, TargetConnection), CancellationToken.None);

        public async Task<NpgsqlConnection> OpenTargetAsync()
        {
            var connection = new NpgsqlConnection(TargetConnection);
            await connection.OpenAsync();
            return connection;
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(adminConnection);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"DROP DATABASE {database} WITH (FORCE);");
            key.Dispose();
            directory.Delete(recursive:true);
        }
    }
}
