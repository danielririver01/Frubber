using System.Text.Json;
using FrubberPOS.Application.Backup;
using FrubberPOS.Application.Persistence;
using Npgsql;

namespace FrubberPOS.Infrastructure.Backup;

/// <summary>
/// Sidecar status for the Respaldo page (JHO-81): database size from
/// <c>pg_database_size</c> plus a tiny JSON sidecar with the last
/// successful backup. Not a history table — only the latest run, enough
/// for KPIs and the health banner.
/// </summary>
public sealed class PostgresBackupStatusService : IBackupStatusService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions database;

    public PostgresBackupStatusService(DatabaseOptions database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
    }

    public BackupOverview GetOverview()
    {
        long size = 0;
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(database.ConnectionString);
            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_database_size(current_database());";
            size = (long)command.ExecuteScalar()!;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
        {
            // Base no disponible aún: reportar tamaño cero.
            size = 0;
        }

        string? fileName = null;
        string? path = null;
        DateTimeOffset? utc = null;
        var sidecar = SidecarPath();
        if (File.Exists(sidecar))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<SidecarPayload>(File.ReadAllText(sidecar), JsonOptions);
                if (payload is not null && !string.IsNullOrWhiteSpace(payload.FileName))
                {
                    fileName = payload.FileName;
                    path = payload.Path;
                    if (payload.LastBackupUtc is not null)
                    {
                        utc = DateTimeOffset.Parse(
                            payload.LastBackupUtc,
                            global::System.Globalization.CultureInfo.InvariantCulture,
                            global::System.Globalization.DateTimeStyles.RoundtripKind);
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Sidecar corrupto o ilegible: se ignora y se reporta "Nunca".
            }
        }

        return new BackupOverview(size, fileName, path, utc);
    }

    public void RecordSuccess(string fileName, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var payload = new SidecarPayload(
            fileName,
            destinationPath,
            DateTimeOffset.UtcNow.ToString("O"));
        var directory = Path.GetDirectoryName(SidecarPath());
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SidecarPath(), JsonSerializer.Serialize(payload, JsonOptions));
    }

    private string SidecarPath()
    {
        // Sidecar lives next to the log directory so it survives backup
        // file moves and isn't tied to a single database file path.
        var logDir = Path.Combine(Path.GetDirectoryName(database.ConnectionString) ?? string.Empty, "logs");
        return Path.Combine(logDir ?? string.Empty, "last-backup.json");
    }

    private sealed record SidecarPayload(string FileName, string Path, string LastBackupUtc);
}
