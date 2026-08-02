using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Services;
using FluentAssertions;
using System.IO;

namespace Bakery.IntegrationTests;

public sealed class ApplicationPathServiceTests
{
    [Fact]
    public void MutableRuntimePaths_AreCentralizedUnderConfiguredWritableRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "BakeryERP", "ApplicationPathServiceTests", Guid.NewGuid().ToString("N"));

        try
        {
            IApplicationPathService paths = new ApplicationPathService(root);
            paths.EnsureDirectoriesExist();

            var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            var mutablePaths = new[]
            {
                paths.LogsDirectory,
                paths.AttachmentsDirectory,
                paths.TempReportsDirectory,
                paths.DocumentsDirectory,
                paths.TemplatesDirectory,
                paths.LogosDirectory,
                paths.GridSettingsFile,
                paths.RestoreWorkDirectory,
                paths.BackupDownloadsDirectory,
                paths.UserConfigurationFile
            };

            mutablePaths.Should().OnlyContain(path =>
                Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase));
            paths.ContentDirectories.Should().BeEquivalentTo(
                paths.AttachmentsDirectory,
                paths.DocumentsDirectory,
                paths.TemplatesDirectory,
                paths.LogosDirectory);
            mutablePaths
                .Where(path => path != paths.GridSettingsFile && path != paths.UserConfigurationFile)
                .Should().OnlyContain(path => Directory.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MutablePathConsumers_DoNotUseExecutableDirectory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceFiles = new[]
        {
            Path.Combine(repositoryRoot, "Bakery.Infrastructure", "Services", "Files", "AttachmentStorageService.cs"),
            Path.Combine(repositoryRoot, "Bakery.Infrastructure", "Services", "Backup", "BackupService.cs"),
            Path.Combine(repositoryRoot, "Bakery.Infrastructure", "Services", "Backup", "BackupRestoreService.cs"),
            Path.Combine(repositoryRoot, "Bakery.WPF", "ViewModels", "Reports", "ReportDetailsViewModel.cs"),
            Path.Combine(repositoryRoot, "Bakery.WPF", "ViewModels", "Settings", "RecoveryViewModel.cs"),
            Path.Combine(repositoryRoot, "Bakery.WPF", "Helpers", "DataGridPersistence.cs")
        };

        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            source.Should().NotContain("AppContext.BaseDirectory");
            source.Should().NotContain("AppDomain.CurrentDomain.BaseDirectory");
        }

        File.ReadAllText(Path.Combine(repositoryRoot, "Bakery.WPF", "appsettings.defaults.json"))
            .Should().NotContain("Logs/bakery-erp-.log");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BakeryERP.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Bakery ERP repository root.");
    }
}
