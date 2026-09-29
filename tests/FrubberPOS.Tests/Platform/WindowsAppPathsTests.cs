using FrubberPOS.Infrastructure.System;

namespace FrubberPOS.Tests.Platform;

public sealed class WindowsAppPathsTests
{
    [Fact]
    public void TempRoot_PathsStayUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new WindowsAppPaths(root);

            Assert.Equal(root, paths.RootDirectory);
            Assert.Equal(Path.Combine(root, "frubberpos.db"), paths.DatabaseFilePath);
            Assert.Equal(Path.Combine(root, "Logs"), paths.LogDirectory);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void EnsureCreated_CreatesRootAndLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new WindowsAppPaths(root);

            paths.EnsureCreated();
            paths.EnsureCreated();

            Assert.True(Directory.Exists(paths.RootDirectory));
            Assert.True(Directory.Exists(paths.LogDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DefaultRoot_IsProgramDataFrubberPOS()
    {
        var paths = new WindowsAppPaths();

        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "FrubberPOS"),
            paths.RootDirectory);
    }

    [Fact]
    public void EmptyRoot_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new WindowsAppPaths("  "));
    }
}
