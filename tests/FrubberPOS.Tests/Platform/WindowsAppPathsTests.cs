using FrubberPOS.Infrastructure.System;

namespace FrubberPOS.Tests.Platform;

public sealed class WindowsAppPathsTests
{
    [Fact]
    public void ExplicitCtor_ConnectionStringExposed()
    {
        var root = Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N"));
        try
        {
            var cs = "Host=localhost;Port=5432;Database=frubberpos_test_pruebas;Username=frubber;Password=frubber;";
            var paths = new WindowsAppPaths(root, cs);

            Assert.Equal(root, paths.RootDirectory);
            Assert.Equal(cs, paths.DatabaseConnectionString);
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
            var paths = new WindowsAppPaths(root, "Host=localhost;Database=frubberpos");

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
        var previous = Environment.GetEnvironmentVariable("FRUBBERPOS_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("FRUBBERPOS_CONNECTION_STRING", null);
            var paths = new WindowsAppPaths();

            Assert.Equal(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "FrubberPOS"),
                paths.RootDirectory);
            Assert.Equal(
                "Host=localhost;Port=5432;Database=frubberpos;Username=frubber;Password=frubber;",
                paths.DatabaseConnectionString);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRUBBERPOS_CONNECTION_STRING", previous);
        }
    }

    [Fact]
    public void EmptyRoot_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new WindowsAppPaths("  ", "Host=localhost;Database=x"));
    }

    [Fact]
    public void EmptyConnectionString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new WindowsAppPaths(Path.GetTempPath(), "   "));
    }

    [Fact]
    public void ConnectionStringReadFromEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable("FRUBBERPOS_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("FRUBBERPOS_CONNECTION_STRING", "Host=otro;Database=frubberpos_demo");
            var paths = new WindowsAppPaths();

            Assert.Equal("Host=otro;Database=frubberpos_demo", paths.DatabaseConnectionString);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRUBBERPOS_CONNECTION_STRING", previous);
        }
    }
}
