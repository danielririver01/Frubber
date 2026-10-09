using System.Text.Json;
using FrubberPOS.Application.Backup;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Infrastructure.Backup;

/// <summary>
/// Sidecar status for the Respaldo page (JHO-81): database file size plus a
/// tiny JSON next to the database with the last successful backup. Not a
/// history table — only the latest run, enough for KPIs and the health banner.
/// </summary>
public sealed class SqliteBackupStatusService : IBackupStatusService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions database;

    public SqliteBackupStatusService(DatabaseOptions database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
    }

    public BackupOverview GetOverview()
    {
        long size = 0;
        var dbPath = database.DatabaseFilePath;
        if (!string.IsNullOrWhiteSpace(dbPath) && File.Exists(dbPath))
        {
            size = new FileInfo(dbPath).Length;
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

    private string SidecarPath() => database.DatabaseFilePath + ".last-backup.json";

    private sealed record SidecarPayload(string FileName, string Path, string LastBackupUtc);
}
