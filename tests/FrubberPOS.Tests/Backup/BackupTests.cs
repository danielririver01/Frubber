using FrubberPOS.Application.Backup;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Logging;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Infrastructure.Backup;
using FrubberPOS.Infrastructure.Logging;
using FrubberPOS.Tests.Products;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Backup;

/// <summary>
/// Manual backup (JHO-53, P-UX §16): SQLite Backup API into a temp file,
/// atomic publish, success names file+location, failure states the
/// original database was untouched and leaves no partial file. Admin-only;
/// no restore exists anywhere in the product.
/// </summary>
public sealed class BackupTests : IDisposable
{
    private readonly string workDir = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N"));

    private (CreateBackupUseCase useCase, IAppLogger logger) BuildBed(
        ProductTestBed bed, FakeCurrentSession? session = null)
    {
        Directory.CreateDirectory(workDir);
        var logger = SerilogLoggerFactory.Create(new LoggingOptions(Path.Combine(workDir, "logs")));
        var service = new SqliteBackupService(new FrubberPOS.Application.Persistence.DatabaseOptions(bed.DbPath));
        return (new CreateBackupUseCase(service, session ?? ProductTestBed.AdminSession(), logger), logger);
    }

    private string Destination(string name = "respaldo.db") => Path.Combine(workDir, name);

    [Fact]
    public async Task Backup_ProducesAnIntactCopy_AndNamesIt()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (useCase, logger) = BuildBed(bed);
        var destination = Destination();

        var result = await useCase.ExecuteAsync(new CreateBackupRequest(destination), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("respaldo.db", result.Value.FileName);
        Assert.Equal(destination, result.Value.DestinationPath);
        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(destination + ".tmp")); // publicación atómica completada

        // La copia abre y contiene los mismos datos.
        long copiedTables;
        await using (var copy = new SqliteConnection($"Data Source={destination};Mode=ReadOnly;Pooling=false"))
        {
            await copy.OpenAsync();
            copiedTables = await Dapper.SqlMapper.ExecuteScalarAsync<long>(
                copy, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';");
            var migratedUsers = await Dapper.SqlMapper.ExecuteScalarAsync<long>(
                copy, "SELECT COUNT(*) FROM SystemSequence;");
            Assert.True(migratedUsers > 0);
        }

        Assert.True(copiedTables > 0);

        // La original sigue operativa.
        await using var original = new SqliteConnection($"Data Source={bed.DbPath};Mode=ReadOnly;Pooling=false");
        await original.OpenAsync();
        Assert.True(await Dapper.SqlMapper.ExecuteScalarAsync<long>(
            original, "SELECT COUNT(*) FROM SystemSequence;") > 0);
        logger.Dispose();
    }

    [Fact]
    public async Task Failure_StatesOriginalUntouched_AndLeavesNoPartialFile()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (useCase, logger) = BuildBed(bed);
        var destination = Path.Combine(workDir, "no-existe", "respaldo.db"); // directorio inexistente

        var result = await useCase.ExecuteAsync(new CreateBackupRequest(destination), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Backup, result.Error!.Code);
        Assert.Contains("La base de datos original no fue alterada", result.Error.Message);
        Assert.Matches(@"Código [0-9A-F]{8}", result.Error.Message); // referencia diagnóstica
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".tmp"));
        logger.Dispose();
    }

    [Fact]
    public async Task EmptyDestination_IsRejected()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (useCase, logger) = BuildBed(bed);

        var result = await useCase.ExecuteAsync(new CreateBackupRequest("  "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        logger.Dispose();
    }

    [Fact]
    public async Task Cashier_IsDenied()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (useCase, logger) = BuildBed(bed, ProductTestBed.CashierSession());

        var result = await useCase.ExecuteAsync(new CreateBackupRequest(Destination()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Authorization, result.Error!.Code);
        Assert.False(File.Exists(Destination()));
        logger.Dispose();
    }

    [Fact]
    public async Task ViewModel_Cancel_ShowsNothing()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (useCase, logger) = BuildBed(bed);
        var nav = new FakeNavigator { NextBackupDestination = null };
        var vm = new BackupViewModel(useCase, nav);

        await vm.CreateAsync();

        Assert.Equal(1, nav.AskBackupDestinationCount);
        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.Equal(string.Empty, vm.ErrorMessage);
        Assert.False(vm.Busy);
        logger.Dispose();
    }

    [Fact]
    public async Task ViewModel_Success_NamesFileAndLocation()
    {
        await using var bed = await ProductTestBed.CreateAsync();
        var (useCase, logger) = BuildBed(bed);
        var destination = Destination("respaldo-manual.db");
        var nav = new FakeNavigator { NextBackupDestination = destination };
        var vm = new BackupViewModel(useCase, nav);

        await vm.CreateAsync();

        Assert.Contains("respaldo-manual.db", vm.StatusMessage);
        Assert.Contains(destination, vm.StatusMessage);
        Assert.Equal(string.Empty, vm.ErrorMessage);
        logger.Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(workDir))
        {
            Directory.Delete(workDir, recursive: true);
        }
    }
}
