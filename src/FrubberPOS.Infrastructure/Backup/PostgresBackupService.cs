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
            // JHO-113: --dbname must be a *libpq* conninfo string
            // (key='value' pairs); passing the Npgsql ';key=value' form made
            // libpq reject it ("opción de conexión no válida \"Host\"") and
            // every manual backup failed.
            var conninfo = new List<string>();
            if (!string.IsNullOrEmpty(builder.Host))
            {
                conninfo.Add($"host='{builder.Host}'");
            }

            if (builder.Port > 0)
            {
                conninfo.Add($"port='{builder.Port}'");
            }

            if (!string.IsNullOrEmpty(builder.Database))
            {
                conninfo.Add($"dbname='{builder.Database}'");
            }

            if (!string.IsNullOrEmpty(builder.Username))
            {
                conninfo.Add($"user='{builder.Username}'");
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ResolvePgDumpExecutable(),
                    Arguments = $"--dbname=\"{string.Join(" ", conninfo)}\" --file=\"{temporary}\" --no-password",
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

    /// <summary>
    /// Resolves <c>pg_dump</c> without assuming it is on PATH. The POS box
    /// ships its own PostgreSQL engine and the Windows installer does not
    /// always add <c>bin</c> to PATH, yet the manual backup (JHO-53) has to
    /// work there; PATH wins when it does have it.
    /// </summary>
    private static string ResolvePgDumpExecutable()
    {
        var fileName = OperatingSystem.IsWindows() ? "pg_dump.exe" : "pg_dump";

        foreach (var directory in PathDirectories())
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            return fileName;
        }

        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
        {
            var postgres = Path.Combine(root, "PostgreSQL");
            if (!Directory.Exists(postgres))
            {
                continue;
            }

            // Highest version first: 18 before 17, …
            foreach (var install in Directory.GetDirectories(postgres)
                         .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(install, "bin", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // Fall back to PATH resolution so Process reports the missing tool.
        return fileName;
    }

    private static IEnumerable<string> PathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = entry.Trim().Trim('"');
            if (directory.Length > 0)
            {
                yield return directory;
            }
        }
    }
}
