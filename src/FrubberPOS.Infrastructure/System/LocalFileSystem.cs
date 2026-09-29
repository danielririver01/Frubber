using FrubberPOS.Application.System;

namespace FrubberPOS.Infrastructure.System;

/// <summary>
/// Local file system implementation (JHO-22). Thin wrapper over
/// <c>System.IO</c> so Application code stays testable.
/// </summary>
public sealed class LocalFileSystem : IFileSystem
{
    public void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);

    public bool FileExists(string path) => File.Exists(path);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(path, cancellationToken);

    public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken) =>
        File.WriteAllBytesAsync(path, data, cancellationToken);

    public void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
