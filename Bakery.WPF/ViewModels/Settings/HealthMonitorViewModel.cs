using Bakery.Application.Interfaces;
using Bakery.Shared.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;

namespace Bakery.WPF.ViewModels;

public sealed partial class HealthMonitorViewModel : ViewModelBase
{
    private readonly IBackupService _backupService;
    private readonly IRecoveryService _recoveryService;

    public HealthMonitorViewModel(IBackupService backupService, IRecoveryService recoveryService)
    {
        _backupService = backupService;
        _recoveryService = recoveryService;
        Title = Loc.OfflineHealthMonitor;
        _ = RefreshAsync();
    }

    [ObservableProperty] private string databaseStatus = "جاري التحقق...";
    [ObservableProperty] private string lastBackupDate = Loc.Unknown;
    [ObservableProperty] private string pendingRecoveries = "جاري التحقق...";
    [ObservableProperty] private string diskSpace = "جاري التحقق...";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            DatabaseStatus = Loc.Online;
            var backups = await _backupService.GetBackupHistoryAsync();
            var latest = backups.FirstOrDefault();
            LastBackupDate = latest != null ? latest.CreatedAt.ToString("f") : Loc.NoBackupsFound;

            var drafts = await _recoveryService.GetAvailableDraftKeysAsync();
            PendingRecoveries = drafts.Any() ? $"تم العثور على {drafts.Count()} مسودات" : "لا يوجد مسودات معلقة";

            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            DiskSpace = $"{drive.AvailableFreeSpace / (1024 * 1024 * 1024)} جيجابايت متوفرة";
        }
        catch (Exception ex)
        {
            DatabaseStatus = Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Health monitor refresh");
        }
    }
}
