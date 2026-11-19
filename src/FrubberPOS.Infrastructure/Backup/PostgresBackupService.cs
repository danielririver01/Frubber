using System.Diagnostics;
using System.Globalization;
using System.Text;
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

    public Task BackupAsync(string destinationFilePath, CancellationToken cancellationToken) =>
        BackupAsync(destinationFilePath, verify: false, cancellationToken);

    public async Task BackupBeforeMigrationAsync(string backupDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(backupDirectory);
        var fileName = FormattableString.Invariant($"premigracion-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.sql");
        await BackupAsync(Path.Combine(backupDirectory, fileName), verify: true, cancellationToken);
    }

    private async Task BackupAsync(string destinationFilePath, bool verify, CancellationToken cancellationToken)
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

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ResolvePostgresExecutable("pg_dump"),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("--file");
            process.StartInfo.ArgumentList.Add(temporary);
            process.StartInfo.ArgumentList.Add("--no-password");
            // Keep libpq pointed at the same server and database as Npgsql.
            process.StartInfo.Environment.Remove("PGHOSTADDR");
            process.StartInfo.Environment.Remove("PGSERVICE");
            process.StartInfo.Environment["PGSSLMODE"] = builder.SslMode switch
            {
                SslMode.VerifyFull => "verify-full",
                SslMode.VerifyCA => "verify-ca",
                _ => builder.SslMode.ToString().ToLowerInvariant(),
            };
            if (!string.IsNullOrEmpty(builder.RootCertificate))
                process.StartInfo.Environment["PGSSLROOTCERT"] = builder.RootCertificate;
            if (!string.IsNullOrEmpty(builder.SslCertificate))
                process.StartInfo.Environment["PGSSLCERT"] = builder.SslCertificate;
            if (!string.IsNullOrEmpty(builder.SslKey))
                process.StartInfo.Environment["PGSSLKEY"] = builder.SslKey;

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
                process.StartInfo.EnvironmentVariables["PGPORT"] = builder.Port.ToString(CultureInfo.InvariantCulture);
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
            var diagnostics = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                }
                await process.WaitForExitAsync();
                throw;
            }
            finally
            {
                await Task.WhenAll(diagnostics, output);
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"pg_dump falló (código {process.ExitCode}).");
            }

            if (verify)
                await VerifySqlDumpAsync(temporary, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
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

    private static async Task VerifySqlDumpAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        var length = (int)Math.Min(file.Length, 4096);
        var buffer = new byte[length];
        await file.ReadExactlyAsync(buffer, cancellationToken);
        if (!Encoding.UTF8.GetString(buffer).Contains("-- PostgreSQL database dump", StringComparison.Ordinal))
            throw new InvalidDataException("El respaldo no contiene el encabezado de PostgreSQL.");

        file.Seek(-length, SeekOrigin.End);
        await file.ReadExactlyAsync(buffer, cancellationToken);
        if (!Encoding.UTF8.GetString(buffer).Split('\n')
            .Any(line => line.TrimEnd('\r') == "-- PostgreSQL database dump complete"))
            throw new InvalidDataException("El respaldo quedó incompleto.");
    }

    /// <summary>
    /// Resolves <c>pg_dump</c> without assuming it is on PATH. The POS box
    /// ships its own PostgreSQL engine and the Windows installer does not
    /// always add <c>bin</c> to PATH, yet the manual backup (JHO-53) has to
    /// work there; PATH wins when it does have it.
    /// </summary>
    internal static string ResolvePostgresExecutable(string name)
    {
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;

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
