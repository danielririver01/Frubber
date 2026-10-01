using FrubberPOS.Application.Backup;
using FrubberPOS.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace FrubberPOS.Infrastructure.Backup;

/// <summary>
/// SQLite Backup API implementation (JHO-53, ADR-013): online copy from
/// the live database into a <c>.tmp</c> sibling, then an atomic replace
/// over the destination. The source file is opened read-only; a failure
/// deletes the temporary file so no partial respaldo remains.
/// </summary>
public sealed class SqliteBackupService : IBackupService
{
    private readonly DatabaseOptions database;

    public SqliteBackupService(DatabaseOptions database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
    }

    public async Task BackupAsync(string destinationFilePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        var temporary = destinationFilePath + ".tmp";
        if (File.Exists(temporary))
        {
            File.Delete(temporary);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (var source = new SqliteConnection($"Data Source={database.DatabaseFilePath};Mode=ReadOnly;Pooling=false"))
            await using (var destination = new SqliteConnection($"Data Source={temporary};Pooling=false"))
            {
                await source.OpenAsync(cancellationToken);
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
            }

            // Publicación final atómica dentro del mismo volumen (§16.1).
            File.Move(temporary, destinationFilePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }
}
