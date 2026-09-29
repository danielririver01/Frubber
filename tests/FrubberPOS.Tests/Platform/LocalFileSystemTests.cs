using FrubberPOS.Infrastructure.System;

namespace FrubberPOS.Tests.Platform;

public sealed class LocalFileSystemTests
{
    [Fact]
    public async Task RoundTrip_WritesReadsAndDeletes()
    {
        var root = NewTempRoot();
        try
        {
            var files = new LocalFileSystem();
            var nested = Path.Combine(root, "a", "b");
            var file = Path.Combine(nested, "datos.bin");
            var data = new byte[] { 1, 2, 3, 250 };

            files.EnsureDirectoryExists(nested);
            Assert.False(files.FileExists(file));

            await files.WriteAllBytesAsync(file, data, CancellationToken.None);
            Assert.True(files.FileExists(file));
            Assert.Equal(data, await files.ReadAllBytesAsync(file, CancellationToken.None));

            files.DeleteFileIfExists(file);
            Assert.False(files.FileExists(file));
            files.DeleteFileIfExists(file);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        var root = NewTempRoot();
        try
        {
            var files = new LocalFileSystem();
            files.EnsureDirectoryExists(root);
            var file = Path.Combine(root, "x.bin");
            await files.WriteAllBytesAsync(file, [9], CancellationToken.None);

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => files.ReadAllBytesAsync(file, canceled.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N"));
}
