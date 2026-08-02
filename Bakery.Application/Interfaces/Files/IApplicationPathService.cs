namespace Bakery.Application.Interfaces;

public interface IApplicationPathService
{
    string RootDirectory { get; }
    string LogsDirectory { get; }
    string AttachmentsDirectory { get; }
    string TempReportsDirectory { get; }
    string DocumentsDirectory { get; }
    string TemplatesDirectory { get; }
    string LogosDirectory { get; }
    string GridSettingsFile { get; }
    string RestoreWorkDirectory { get; }
    string BackupDownloadsDirectory { get; }
    string BackupEncryptionKeyFile { get; }
    string UserConfigurationFile { get; }

    IReadOnlyList<string> ContentDirectories { get; }
    void EnsureDirectoriesExist();
}

public static class ApplicationPathDefaults
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BakeryERP");

    public static string LogsDirectory => Path.Combine(RootDirectory, "Logs");
    public static string GridSettingsFile => Path.Combine(RootDirectory, "grid_settings.json");
    public static string UserConfigurationFile => Path.Combine(RootDirectory, "appsettings.user.json");
}
