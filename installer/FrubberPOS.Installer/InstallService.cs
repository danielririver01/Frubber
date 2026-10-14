using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Microsoft.Win32;

namespace FrubberPOS.Installer;

/// <summary>
/// Motor real del asistente (JHO-93): extrae el payload embebido (la app
/// publicada), crea accesos directos y registra la desinstalación en
/// Windows. NUNCA toca C:\ProgramData\FrubberPOS — los datos del negocio
/// se conservan incluso al desinstalar (Guía §4).
/// </summary>
public sealed class InstallService
{
    public const string AppExeName = "FrubberPOS.Desktop.exe";
    public const string AppName = "Frubber POS";
    public const string Version = "1.0.0";
    public const string UninstallerName = "FrubberPOS-Desinstalar.exe";

    private static string UninstallKeyPath => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FrubberPOS";

    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

    public static bool IsInstalled() => File.Exists(Path.Combine(DefaultInstallDir, AppExeName));

    public static string? GetInstalledLocation() =>
        Registry.LocalMachine.OpenSubKey(UninstallKeyPath)?.GetValue("InstallLocation") as string;

    // ---------- Instalación ----------

    public async Task InstallAsync(
        string targetDir,
        bool createDesktopShortcut,
        IProgress<(double Fraction, string Stage)> progress,
        CancellationToken cancellationToken)
    {
        progress.Report((0.02, "Preparando los archivos…"));

        // 1. Leer el payload embebido (artifacts\publish comprimido en zip).
        var tempZip = Path.Combine(Path.GetTempPath(), $"frubberpos-payload-{Guid.NewGuid():N}.zip");
        var assembly = typeof(InstallService).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Este instalador no contiene el paquete de la aplicación. Descárgalo de nuevo completo.");
        await using (var resource = assembly.GetManifestResourceStream(resourceName)!)
        await using (var file = File.Create(tempZip))
        {
            await resource.CopyToAsync(file, cancellationToken);
        }

        // 2. Extraer a un temporal y copiar archivo por archivo (progreso real).
        var tempDir = Path.Combine(Path.GetTempPath(), $"frubberpos-extract-{Guid.NewGuid():N}");
        try
        {
            progress.Report((0.08, "Extrayendo los archivos…"));
            ZipFile.ExtractToDirectory(tempZip, tempDir);

            Directory.CreateDirectory(targetDir);
            var files = Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories).ToList();
            var total = files.Count;
            var done = 0;
            foreach (var source in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(tempDir, source);
                var target = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // File.Replace falla si la app está corriendo: el error se traduce arriba.
                File.Copy(source, target, overwrite: true);
                done++;
                progress.Report((0.08 + 0.80 * done / total,
                    done >= total ? "Archivos copiados." : $"Copiando archivos ({done} de {total})…"));
            }
        }
        finally
        {
            TryDelete(tempZip);
            if (Directory.Exists(tempDir)) TryDelete(tempDir);
        }

        // 3. El propio instalador queda copiado como desinstalador dentro de la carpeta.
        progress.Report((0.9, "Creando los accesos directos…"));
        var selfExe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("No se pudo ubicar el ejecutable del asistente.");
        var uninstallerPath = Path.Combine(targetDir, UninstallerName);
        if (!string.Equals(selfExe, uninstallerPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(selfExe, uninstallerPath, overwrite: true);
        }

        // 4. Accesos directos: menú inicio siempre, escritorio si se pidió.
        var exePath = Path.Combine(targetDir, AppExeName);
        var startMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
        Directory.CreateDirectory(startMenuDir);
        CreateShortcut(Path.Combine(startMenuDir, AppName + ".lnk"), exePath, targetDir);
        CreateShortcut(Path.Combine(startMenuDir, "Desinstalar " + AppName + ".lnk"), uninstallerPath, targetDir);
        if (createDesktopShortcut)
        {
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk"),
                exePath, targetDir);
        }

        // 5. Registro de «Agregar o quitar programas».
        progress.Report((0.96, "Registrando en Windows…"));
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath, writable: true);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "Frubber");
        key.SetValue("DisplayIcon", exePath, RegistryValueKind.ExpandString);
        key.SetValue("InstallLocation", targetDir, RegistryValueKind.ExpandString);
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall", RegistryValueKind.ExpandString);
        key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall --quiet", RegistryValueKind.ExpandString);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(DirectorySize(targetDir) / 1024), RegistryValueKind.DWord);

        progress.Report((1.0, "Instalación lista."));
    }

    // ---------- Desinstalación (regla: jamás borrar C:\ProgramData\FrubberPOS) ----------

    public async Task UninstallAsync(string installDir, IProgress<(double Fraction, string Stage)> progress, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(installDir)) return;

        progress.Report((0.1, "Cerrando Frubber POS si está abierto…"));
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppExeName)))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(4_000);
            }
            catch { /* si no cierra, el borrado dirá qué archivo quedó en uso */ }
        }

        progress.Report((0.35, "Quitando el programa…"));
        var files = Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories).ToList();
        foreach (var source in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(source);
        }
        TryDelete(installDir);

        progress.Report((0.75, "Quitando los accesos directos…"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk"));

        progress.Report((0.9, "Limpiando el registro…"));
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

        // El desinstalador copiado dentro de la carpeta instalada no puede borrarse a sí mismo:
        // si algo quedó bloqueado, programa el borrado tras salir.
        if (Directory.Exists(installDir))
        {
            var leftover = Path.Combine(installDir, UninstallerName);
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c timeout /t 2 /nobreak >nul & rd /s /q \"{installDir}\"")
            { CreateNoWindow = true })?.Dispose();
            _ = leftover; // Program Files desaparece con la carpeta completa
        }

        progress.Report((1.0, "Desinstalación terminada. Tus datos siguen en C:\\ProgramData\\FrubberPOS."));
        await Task.Delay(150, cancellationToken);
    }

    private static void CreateShortcut(string path, string target, string workDir)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell no está disponible en este Windows.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.WorkingDirectory = workDir;
        link.IconLocation = target + ",0";
        link.Description = AppName;
        link.Save();
    }

    private static long DirectorySize(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { /* el flujo principal decide si es bloqueante */ }
    }
}
