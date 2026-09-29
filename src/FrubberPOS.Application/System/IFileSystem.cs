namespace FrubberPOS.Application.System;

/// <summary>
/// Local file system abstraction. Application depends on this interface,
/// never on <c>System.IO</c> static APIs or Windows paths directly.
/// </summary>
public interface IFileSystem
{
    void EnsureDirectoryExists(string path);

    bool FileExists(string path);

    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken);

    void DeleteFileIfExists(string path);
}
