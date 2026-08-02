using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Settings.Configuration;

namespace Bakery.IntegrationTests;

public sealed class StartupLoggingTests
{
    [Fact]
    public void SerilogConfiguration_ResolvesFileSinkWithoutDependencyContextScanning()
    {
        var appSettingsPath = FindRepositoryFile("Bakery.WPF", "appsettings.defaults.json");
        var tempRoot = Path.Combine(Path.GetTempPath(), "BakeryERP", "StartupLoggingTests", Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(tempRoot, "single-file-.log");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(appSettingsPath, optional: false)
                .Build();

            configuration["Serilog:Using:0"].Should().Be("Serilog.Sinks.File");

            var readerOptions = new ConfigurationReaderOptions(
                typeof(FileLoggerConfigurationExtensions).Assembly);
            using var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration, readerOptions)
                .WriteTo.File(logPath, shared: true)
                .CreateLogger();

            logger.Information("Single-file compatible startup logging regression test");

            Directory.GetFiles(tempRoot, "single-file-*.log").Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
