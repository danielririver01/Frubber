using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FrubberPOS.Installer;

/// <summary>
/// Motor real del asistente (JHO-93): extrae el payload embebido (la app
/// publicada), crea accesos directos y registra la desinstalación en
/// Windows. NUNCA toca C:\ProgramData\FrubberPOS — los datos del negocio
/// se conservan incluso al desinstalar (Guía §4).
/// JHO-95: la raíz de un disco nunca es destino ni objetivo de borrado, y el
/// tamaño declarado sale SOLO de los archivos que nosotros copiamos.
/// </summary>
public sealed class InstallService
{
    public const string AppExeName = "FrubberPOS.Desktop.exe";
    public const string AppName = "Frubber POS";
    public const string Version = "1.0.1";
    public const string UninstallerName = "FrubberPOS-Desinstalar.exe";

    private static string UninstallKeyPath => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FrubberPOS";

    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

    public static bool IsInstalled() => File.Exists(Path.Combine(DefaultInstallDir, AppExeName));

    public static string? GetInstalledLocation() =>
        Registry.LocalMachine.OpenSubKey(UninstallKeyPath)?.GetValue("InstallLocation") as string;

    // ---------- Instalación ----------

    /// <summary>
    /// JHO-95: si el usuario eligió la raíz de un disco (F:\) o la raíz de un
    /// recurso compartido, se instala en la subcarpeta «Frubber POS». Así nunca
    /// se sueltan archivos sueltos en la raíz y jamás se recorre el disco completo.
    /// </summary>
    public static string NormalizeTargetDir(string target)
    {
        target = target.Trim();
        if (target.Length == 0) return target;

        var root = Path.GetPathRoot(target);
        if (string.IsNullOrEmpty(root)) return target;

        return IsSameAsRoot(target, root)
            ? Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar + AppName
            : target;
    }

    private static bool IsSameAsRoot(string path, string? root = null)
    {
        root ??= Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return false;
        return string.Equals(path.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    public async Task InstallAsync(
        string targetDir,
        bool createDesktopShortcut,
        IProgress<(double Fraction, string Stage)> progress,
        CancellationToken cancellationToken)
    {
        // JHO-95: la raíz de un disco jamás es destino directo.
        targetDir = NormalizeTargetDir(targetDir);

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
        long copiedBytes = 0;
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
                copiedBytes += new FileInfo(source).Length;
                done++;
                progress.Report((0.08 + 0.80 * done / total,
                    done >= total ? "Archivos copiados." : $"Copiando archivos ({done} de {total})…"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallFailedException(
                "No se pudieron copiar los archivos. Cierra Frubber POS si está abierto, revisa la carpeta y el espacio disponible, e intenta de nuevo.",
                ex);
        }
        finally
        {
            TryDelete(tempZip);
            if (Directory.Exists(tempDir)) TryDelete(tempDir);
        }

        // 3. El propio instalador queda copiado como desinstalador dentro de la carpeta,
        //    y 4. los accesos directos: menú inicio siempre, escritorio si se pidió.
        progress.Report((0.9, "Creando los accesos directos…"));
        var selfExe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("No se pudo ubicar el ejecutable del asistente.");
        var exePath = Path.Combine(targetDir, AppExeName);
        var uninstallerPath = Path.Combine(targetDir, UninstallerName);
        try
        {
            if (!string.Equals(selfExe, uninstallerPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(selfExe, uninstallerPath, overwrite: true);
                copiedBytes += new FileInfo(selfExe).Length;
            }

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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            throw new InstallFailedException(
                "No se pudieron crear los accesos directos ni el desinstalador. Cierra Frubber POS si está abierto e intenta de nuevo.",
                ex);
        }

        // 5. Registro de «Agregar o quitar programas».
        progress.Report((0.96, "Registrando en Windows…"));
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath, writable: true)
                ?? throw new InstallFailedException(
                    "Windows no permitió registrar el programa (¿antivirus o política del equipo?). Intenta de nuevo; si continúa, avísanos.", null);
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "Frubber");
            key.SetValue("DisplayIcon", exePath, RegistryValueKind.ExpandString);
            key.SetValue("InstallLocation", targetDir, RegistryValueKind.ExpandString);
            key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall", RegistryValueKind.ExpandString);
            key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall --quiet", RegistryValueKind.ExpandString);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            // JHO-95: solo los bytes que NOSOTROS copiamos — recorrer la carpeta
            // destino equivale, en una raíz de disco, a recorrer TODO el disco y
            // revienta con carpetas protegidas como «System Volume Information».
            key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, copiedBytes / 1024), RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InstallFailedException(
                "Windows no pudo registrar el programa para la desinstalación (¿antivirus o política del equipo?). Los archivos ya están copiados: intenta de nuevo.",
                ex);
        }

        progress.Report((1.0, "Instalación lista."));
    }

    // ---------- Desinstalación (regla: jamás borrar C:\ProgramData\FrubberPOS) ----------

    /// <summary>Síncrono a propósito: ejecutar desde un hilo de fondo (Task.Run)
    /// para no bloquear el hilo de UI de WPF.</summary>
    public void Uninstall(string installDir, IProgress<(double Fraction, string Stage)> progress, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(installDir)) return;

        // JHO-95 (seguridad de datos): jamás borrar la raíz de un disco ni una
        // carpeta que no contiene la app. Un InstallLocation antiguo apuntando a
        // "F:\" hubiera borrado TODO el contenido del disco del cliente.
        if (IsSameAsRoot(installDir) || !File.Exists(Path.Combine(installDir, AppExeName)))
        {
            progress.Report((1.0, "Esta carpeta no es una instalación de Frubber POS: no se borró nada. Si ya no la necesitas, elimínala manualmente."));
            return;
        }

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
