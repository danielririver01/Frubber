using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Startup;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Infrastructure.Migrations;
using FrubberPOS.Infrastructure.Persistence;
using FrubberPOS.Infrastructure.Persistence.Users;
using FrubberPOS.Infrastructure.Security;
using FrubberPOS.Tests.TestDoubles;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Tests.Desktop;

public sealed class FirstStartViewModelTests
{
    [Fact]
    public async Task MismatchedPins_ShowsErrorWithoutTouchingDb()
    {
        await using var db = await CreateDbAsync();
        var navigator = new FakeNavigator();
        var viewModel = new FirstStartViewModel(
            new CreateFirstAdministratorUseCase(db.Users, db.Hasher, db.Units), navigator);

        viewModel.DisplayName = "Dueño";
        viewModel.Pin = "1234";
        viewModel.ConfirmPin = "4321";
        await viewModel.CreateAsync();

        Assert.Equal("Los PIN no coinciden.", viewModel.ErrorMessage);
        Assert.Equal(0, navigator.LoginCount);
    }

    [Fact]
    public async Task Success_NavigatesToLogin()
    {
        await using var db = await CreateDbAsync();
        var navigator = new FakeNavigator();
        var viewModel = new FirstStartViewModel(
            new CreateFirstAdministratorUseCase(db.Users, db.Hasher, db.Units), navigator);

        viewModel.DisplayName = "Dueño";
        viewModel.Pin = "1234";
        viewModel.ConfirmPin = "1234";
        await viewModel.CreateAsync();

        Assert.Equal(string.Empty, viewModel.ErrorMessage);
        Assert.Equal(1, navigator.LoginCount);
    }

    private sealed record Db(DapperUserRepository Users, Pbkdf2PinHasher Hasher, SqliteUnitOfWorkFactory Units, string Path)
    {
        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            foreach (var journal in new[] { Path + "-wal", Path + "-shm", Path + "-journal" })
            {
                if (File.Exists(journal))
                {
                    File.Delete(journal);
                }
            }

            await Task.CompletedTask;
        }
    }

    private static async Task<Db> CreateDbAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N") + ".db");
        var migrated = await new MigrationRunner().MigrateAsync($"Data Source={path}", CancellationToken.None);
        if (!migrated.IsSuccess)
        {
            throw new InvalidOperationException("No se pudo migrar la base de prueba.");
        }

        var units = new SqliteUnitOfWorkFactory(new SqliteConnectionFactory(new DatabaseOptions(path)));
        return new Db(new DapperUserRepository(), new Pbkdf2PinHasher(), units, path);
    }
}
