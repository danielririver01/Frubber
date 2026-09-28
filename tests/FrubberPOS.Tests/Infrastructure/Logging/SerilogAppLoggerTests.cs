using System.Text.RegularExpressions;
using FrubberPOS.Application.Logging;
using FrubberPOS.Infrastructure.Logging;

namespace FrubberPOS.Tests.Infrastructure.Logging;

public sealed class SerilogAppLoggerTests
{
    [Fact]
    public void Create_WritesRollingFileUnderGivenDirectory()
    {
        var directory = NewTempDirectory();
        try
        {
            using (IAppLogger logger = SerilogLoggerFactory.Create(new LoggingOptions(directory)))
            {
                logger.LogInformation("APP_START", "Frubber POS iniciado.");
            }

            var file = Assert.Single(Directory.GetFiles(directory, "log*.txt"));
            var content = File.ReadAllText(file);
            Assert.Contains("APP_START", content, StringComparison.Ordinal);
            Assert.Contains("Frubber POS iniciado.", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LogWarning_EntryContainsWarningLevel()
    {
        var directory = NewTempDirectory();
        try
        {
            using (IAppLogger logger = SerilogLoggerFactory.Create(new LoggingOptions(directory)))
            {
                logger.LogWarning("LICENSE_CLOCK", "Posible retroceso del reloj.");
            }

            var file = Assert.Single(Directory.GetFiles(directory, "log*.txt"));
            var content = File.ReadAllText(file);
            Assert.Contains("[WRN]", content, StringComparison.Ordinal);
            Assert.Contains("LICENSE_CLOCK", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LogError_ReturnsDiagnosticIdWrittenIntoEntry()
    {
        var directory = NewTempDirectory();
        try
        {
            string diagnosticId;
            using (IAppLogger logger = SerilogLoggerFactory.Create(new LoggingOptions(directory)))
            {
                diagnosticId = logger.LogError("DB_FAILURE", "No se pudo abrir la base de datos.", new InvalidOperationException("disk io"));
            }

            Assert.Matches("^[0-9A-F]{8}$", diagnosticId);

            var file = Assert.Single(Directory.GetFiles(directory, "log*.txt"));
            var content = File.ReadAllText(file);
            Assert.Contains(diagnosticId, content, StringComparison.Ordinal);
            Assert.Contains("DB_FAILURE", content, StringComparison.Ordinal);
            Assert.Contains("InvalidOperationException", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Create_WithEmptyDirectory_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SerilogLoggerFactory.Create(new LoggingOptions("  ")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void LoggingOptions_RejectsNonPositiveRetention(int retainedDayCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoggingOptions(NewTempDirectory(), retainedDayCount));
    }

    [Fact]
    public void LoggingOptions_Defaults_MatchArchitecturePolicy()
    {
        var options = new LoggingOptions(NewTempDirectory());

        Assert.Equal(15, options.RetainedDayCount);
        Assert.Equal(10 * 1024 * 1024, options.FileSizeLimitBytes);
    }

    [Fact]
    public void LogInformation_WithEmptyEventCode_ThrowsArgumentException()
    {
        var directory = NewTempDirectory();
        try
        {
            using IAppLogger logger = SerilogLoggerFactory.Create(new LoggingOptions(directory));
            Assert.Throws<ArgumentException>(() => logger.LogInformation(" ", "mensaje"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewTempDirectory() =>
        Path.Combine(Path.GetTempPath(), "FrubberPOS", Guid.NewGuid().ToString("N"));
}
