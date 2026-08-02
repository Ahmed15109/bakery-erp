using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services;

public sealed class ApplicationPathService : IApplicationPathService
{
    public ApplicationPathService(string? rootDirectory = null)
    {
        RootDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(rootDirectory)
            ? ApplicationPathDefaults.RootDirectory
            : rootDirectory);
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
        AttachmentsDirectory = Path.Combine(RootDirectory, "Attachments");
        TempReportsDirectory = Path.Combine(RootDirectory, "TempReports");
        DocumentsDirectory = Path.Combine(RootDirectory, "Documents");
        TemplatesDirectory = Path.Combine(RootDirectory, "Templates");
        LogosDirectory = Path.Combine(RootDirectory, "Logos");
        GridSettingsFile = Path.Combine(RootDirectory, "grid_settings.json");
        RestoreWorkDirectory = Path.Combine(RootDirectory, "RestoreWork");
        BackupDownloadsDirectory = Path.Combine(RootDirectory, "BackupDownloads");
        BackupEncryptionKeyFile = Path.Combine(RootDirectory, "backup-encryption.key");
        UserConfigurationFile = Path.Combine(RootDirectory, "appsettings.user.json");
        ContentDirectories =
        [
            AttachmentsDirectory,
            DocumentsDirectory,
            TemplatesDirectory,
            LogosDirectory
        ];
    }

    public string RootDirectory { get; }
    public string LogsDirectory { get; }
    public string AttachmentsDirectory { get; }
    public string TempReportsDirectory { get; }
    public string DocumentsDirectory { get; }
    public string TemplatesDirectory { get; }
    public string LogosDirectory { get; }
    public string GridSettingsFile { get; }
    public string RestoreWorkDirectory { get; }
    public string BackupDownloadsDirectory { get; }
    public string BackupEncryptionKeyFile { get; }
    public string UserConfigurationFile { get; }
    public IReadOnlyList<string> ContentDirectories { get; }

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(TempReportsDirectory);
        Directory.CreateDirectory(RestoreWorkDirectory);
        Directory.CreateDirectory(BackupDownloadsDirectory);
        foreach (var directory in ContentDirectories)
            Directory.CreateDirectory(directory);
    }
}
