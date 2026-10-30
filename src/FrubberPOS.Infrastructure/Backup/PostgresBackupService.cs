using System.Diagnostics;
using FrubberPOS.Application.Backup;
using FrubberPOS.Application.Persistence;
using Npgsql;

namespace FrubberPOS.Infrastructure.Backup;

/// <summary>
/// PostgreSQL backup implementation (JHO-53, ADR-013): invokes <c>pg_dump</c>
/// against the live database into a <c>.tmp</c> sibling, then an atomic
/// rename over the destination. A failure deletes the temporary file so no
/// partial respaldo remains. The source database is never written to.
/// </summary>
public sealed class PostgresBackupService : IBackupService
{
    private readonly DatabaseOptions database;

    public PostgresBackupService(DatabaseOptions database)
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

            var builder = new NpgsqlConnectionStringBuilder(database.ConnectionString);
            var env = Environment.GetEnvironmentVariables();

            // pg_dump reads connection parameters from environment variables
            // and PGPASSWORD for the password, avoiding command-line exposure.
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pg_dump",
                    Arguments = $"--dbname=\"{builder.ConnectionString}\" --file=\"{temporary}\" --no-password",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (builder.Password is not null)
            {
                process.StartInfo.EnvironmentVariables["PGPASSWORD"] = builder.Password;
            }
            if (builder.Host is not null)
            {
                process.StartInfo.EnvironmentVariables["PGHOST"] = builder.Host;
            }
            if (builder.Port > 0)
            {
                process.StartInfo.EnvironmentVariables["PGPORT"] = builder.Port.ToString();
            }
            if (builder.Database is not null)
            {
                process.StartInfo.EnvironmentVariables["PGDATABASE"] = builder.Database;
            }
            if (builder.Username is not null)
            {
                process.StartInfo.EnvironmentVariables["PGUSER"] = builder.Username;
            }

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"pg_dump falló (código {process.ExitCode}): {error}");
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
