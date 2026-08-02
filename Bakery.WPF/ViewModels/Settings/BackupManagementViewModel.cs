using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Enums;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Bakery.WPF.ViewModels;

public sealed partial class BackupManagementViewModel : ViewModelBase, IDisposable
{
    private readonly IBackupService _backupService;
    private readonly IRestoreService _restoreService;
    private readonly ICloudBackupService _cloudBackupService;
    private readonly IBackupQueueService _backupQueueService;
    private readonly IBackupStatusNotifier _statusNotifier;
    private readonly IPermissionService _permissionService;
    private readonly IMessageService _messageService;
    private readonly IFileLauncherService _fileLauncherService;

    public BackupManagementViewModel(
        IBackupService backupService,
        IRestoreService restoreService,
        ICloudBackupService cloudBackupService,
        IBackupQueueService backupQueueService,
        IBackupStatusNotifier statusNotifier,
        IPermissionService permissionService,
        IMessageService messageService,
        IFileLauncherService fileLauncherService)
    {
        _backupService = backupService;
        _restoreService = restoreService;
        _cloudBackupService = cloudBackupService;
        _backupQueueService = backupQueueService;
        _statusNotifier = statusNotifier;
        _permissionService = permissionService;
        _messageService = messageService;
        _fileLauncherService = fileLauncherService;
        Title = "النسخ الاحتياطي واستعادة البيانات";
        _statusNotifier.StatusChanged += OnStatusChanged;
        _ = LoadAsync();
    }

    [ObservableProperty] private ObservableCollection<BackupMetadata> backups = [];
    [ObservableProperty] private BackupMetadata? selectedBackup;
    [ObservableProperty] private string backupDirectory = string.Empty;
    [ObservableProperty] private bool googleDriveConnected;
    [ObservableProperty] private bool isSameDriveWarning;
    [ObservableProperty] private string statusMessage = "جاري تحميل حالة النسخ الاحتياطي...";

    public bool CanCreateManual => _permissionService.HasPermission(PermissionKeys.BackupCreateManual);
    public bool CanRestore => _permissionService.HasPermission(PermissionKeys.BackupRestore);
    public bool CanDelete => _permissionService.HasPermission(PermissionKeys.BackupDelete);
    public bool CanManageSettings => _permissionService.HasPermission(PermissionKeys.BackupManageSettings);
    public bool CanConnectDrive => _permissionService.HasPermission(PermissionKeys.BackupConnectGoogleDrive);
    public bool CanDisconnectDrive => _permissionService.HasPermission(PermissionKeys.BackupDisconnectGoogleDrive);
    public bool HasSelection => SelectedBackup is not null;
    public string GoogleDriveStatus => GoogleDriveConnected ? "متصل" : "غير متصل";

    partial void OnSelectedBackupChanged(BackupMetadata? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        RestoreCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RetryUploadCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnBackupDirectoryChanged(string value) => OpenFolderCommand.NotifyCanExecuteChanged();

    partial void OnGoogleDriveConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(GoogleDriveStatus));
        ConnectGoogleDriveCommand.NotifyCanExecuteChanged();
        DisconnectGoogleDriveCommand.NotifyCanExecuteChanged();
        RetryUploadCommand.NotifyCanExecuteChanged();
    }

    protected override void OnBusyStateChanged(bool value)
    {
        LoadCommand.NotifyCanExecuteChanged();
        CreateManualCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        RestoreFromFileCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RetryUploadCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        SelectFolderCommand.NotifyCanExecuteChanged();
        ResetFolderCommand.NotifyCanExecuteChanged();
        ConnectGoogleDriveCommand.NotifyCanExecuteChanged();
        DisconnectGoogleDriveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        if (!CanLoad()) return;
        try
        {
            IsBusy = true;
            await RefreshStateAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoad() => !IsBusy;

    private async Task RefreshStateAsync()
    {
        try
        {
            var history = await _backupService.GetBackupHistoryAsync();
            Backups = new ObservableCollection<BackupMetadata>(history);
            if (CanManageSettings)
            {
                var settings = await _backupService.GetSettingsAsync();
                BackupDirectory = settings.BackupDirectory;
                GoogleDriveConnected = settings.GoogleDriveConnected;
                IsSameDriveWarning = settings.IsSameDriveAsDatabase;
            }
            var latest = Backups.FirstOrDefault();
            StatusMessage = latest switch
            {
                null => "لم يتم إنشاء نسخة احتياطية بعد.",
                { Status: BackupStatus.Failed } => "تحتاج آخر محاولة نسخ احتياطي إلى مراجعة.",
                { CloudStatus: CloudBackupStatus.PendingUpload or CloudBackupStatus.UploadFailed } => "النسخة المحلية سليمة والرفع السحابي معلق.",
                _ => "آخر نسخة احتياطية المحلية سليمة."
            };
        }
        catch
        {
            StatusMessage = "تعذر تحميل سجل النسخ الاحتياطي.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateManualBackup))]
    private async Task CreateManualAsync()
    {
        if (!CanCreateManualBackup()) return;
        try
        {
            IsBusy = true;
            await _backupService.CreateBackupAsync();
            _messageService.ShowInfo("تم إنشاء النسخة الاحتياطية المحلية والتحقق منها بنجاح.");
            await RefreshStateAsync();
        }
        catch (UnauthorizedAccessException)
        {
            _messageService.ShowError("ليس لديك صلاحية لإنشاء نسخة احتياطية يدوية.");
        }
        catch
        {
            _messageService.ShowError("تعذر إنشاء النسخة الاحتياطية. بقيت النسخ السابقة دون تغيير.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreateManualBackup() => CanCreateManual && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRestoreSelected))]
    private async Task RestoreAsync()
    {
        if (!CanRestoreSelected()) return;
        var selectedBackup = SelectedBackup!;
        if (!_messageService.Confirm("سيتم إنشاء نسخة أمان ثم استبدال بيانات التطبيق. هل تريد المتابعة؟")) return;
        try
        {
            IsBusy = true;
            var result = selectedBackup.LocalFileAvailable
                ? await _restoreService.RestoreHistoryAsync(selectedBackup.Id)
                : await _restoreService.RestoreCloudAsync(selectedBackup.Id);
            if (!result.Succeeded)
            {
                _messageService.ShowError(result.ErrorSummary ?? "تعذر استعادة النسخة الاحتياطية.");
                return;
            }
            _messageService.ShowInfo("تمت الاستعادة بنجاح. سيعاد تشغيل التطبيق الآن.");
            RestartApplication();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRestoreSelected() => CanRestore && HasSelection && !IsBusy &&
        (SelectedBackup!.LocalFileAvailable || SelectedBackup.GoogleDriveAvailable);

    [RelayCommand(CanExecute = nameof(CanRestoreFromFile))]
    private async Task RestoreFromFileAsync()
    {
        if (!CanRestoreFromFile()) return;
        var dialog = new OpenFileDialog
        {
            Title = "اختيار نسخة احتياطية",
            Filter = "BakeryERP Backup (*.berpbackup;*.zip)|*.berpbackup;*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true ||
            !_messageService.Confirm("سيتم التحقق من الملف وإنشاء نسخة أمان قبل الاستعادة. هل تريد المتابعة؟")) return;
        IsBusy = true;
        try
        {
            var result = await _restoreService.RestoreLocalAsync(dialog.FileName);
            if (!result.Succeeded)
            {
                _messageService.ShowError(result.ErrorSummary ?? "تعذر استعادة النسخة الاحتياطية.");
                return;
            }
            _messageService.ShowInfo("تمت الاستعادة بنجاح. سيعاد تشغيل التطبيق الآن.");
            RestartApplication();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRestoreFromFile() => CanRestore && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteAsync()
    {
        if (!CanDeleteSelected()) return;
        var selectedBackup = SelectedBackup!;
        if (!_messageService.Confirm("حذف ملف النسخة المحلية المحددة؟ سيبقى سجل العملية محفوظاً.")) return;
        try
        {
            IsBusy = true;
            await _backupService.DeleteLocalBackupAsync(selectedBackup.Id);
            await RefreshStateAsync();
        }
        catch
        {
            _messageService.ShowError("تعذر حذف ملف النسخة الاحتياطية.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDeleteSelected() => CanDelete && HasSelection && !IsBusy && SelectedBackup!.LocalFileAvailable;

    [RelayCommand(CanExecute = nameof(CanRetrySelected))]
    private async Task RetryUploadAsync()
    {
        if (!CanRetrySelected()) return;
        var selectedBackup = SelectedBackup!;
        try
        {
            IsBusy = true;
            await _backupQueueService.QueueCloudRetryAsync(selectedBackup.Id);
            StatusMessage = "تمت جدولة محاولة الرفع في الخلفية.";
            await RefreshStateAsync();
        }
        catch
        {
            _messageService.ShowError("تعذر جدولة الرفع. تحقق من الاتصال وربط Google Drive.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRetrySelected() => CanConnectDrive && GoogleDriveConnected && HasSelection && !IsBusy &&
        SelectedBackup!.LocalFileAvailable && !SelectedBackup.GoogleDriveAvailable;

    [RelayCommand(CanExecute = nameof(CanDownloadSelected))]
    private async Task DownloadAsync()
    {
        if (!CanDownloadSelected()) return;
        var selectedBackup = SelectedBackup!;
        var dialog = new SaveFileDialog
        {
            Title = "حفظ النسخة من Google Drive",
            Filter = "BakeryERP Backup (*.berpbackup;*.zip)|*.berpbackup;*.zip",
            FileName = selectedBackup.FileName,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            await _cloudBackupService.DownloadAsync(selectedBackup.GoogleDriveFileId!, dialog.FileName);
            _messageService.ShowInfo("تم تنزيل النسخة الاحتياطية والتحقق من اكتمال الملف.");
        }
        catch
        {
            _messageService.ShowError("تعذر تنزيل النسخة الاحتياطية من Google Drive.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDownloadSelected() => CanRestore && HasSelection && !IsBusy && SelectedBackup!.GoogleDriveAvailable;

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        if (CanOpenFolder()) _fileLauncherService.OpenFile(BackupDirectory);
    }

    private bool CanOpenFolder() => CanManageSettings && !IsBusy && !string.IsNullOrWhiteSpace(BackupDirectory);

    [RelayCommand(CanExecute = nameof(CanManageBackupSettings))]
    private async Task SelectFolderAsync()
    {
        if (!CanManageBackupSettings()) return;
        var dialog = new OpenFolderDialog
        {
            Title = "اختيار مجلد النسخ الاحتياطي",
            InitialDirectory = BackupDirectory,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            await _backupService.SetBackupDirectoryAsync(dialog.FolderName);
            await RefreshStateAsync();
            if (IsSameDriveWarning)
                _messageService.ShowInfo("تم الحفظ. يفضل اختيار قرص آخر أو وسيط خارجي لزيادة الأمان.");
        }
        catch
        {
            _messageService.ShowError("تعذر استخدام المجلد المحدد للنسخ الاحتياطي.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageBackupSettings))]
    private async Task ResetFolderAsync()
    {
        if (!CanManageBackupSettings()) return;
        try
        {
            IsBusy = true;
            await _backupService.SetBackupDirectoryAsync(null);
            await RefreshStateAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanManageBackupSettings() => CanManageSettings && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanConnectGoogleDrive))]
    private async Task ConnectGoogleDriveAsync()
    {
        if (!CanConnectGoogleDrive()) return;
        try
        {
            IsBusy = true;
            await _cloudBackupService.ConnectAsync();
            await RefreshStateAsync();
            _messageService.ShowInfo("تم ربط Google Drive بنجاح.");
        }
        catch
        {
            _messageService.ShowError("تعذر ربط Google Drive. تحقق من إعدادات OAuth ثم حاول مرة أخرى.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnectGoogleDrive() => CanConnectDrive && !GoogleDriveConnected && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDisconnectGoogleDrive))]
    private async Task DisconnectGoogleDriveAsync()
    {
        if (!CanDisconnectGoogleDrive() ||
            !_messageService.Confirm("فصل Google Drive عن هذا الجهاز؟ لن تحذف الملفات الموجودة في السحابة.")) return;
        try
        {
            IsBusy = true;
            await _cloudBackupService.DisconnectAsync();
            await RefreshStateAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDisconnectGoogleDrive() => CanDisconnectDrive && GoogleDriveConnected && !IsBusy;

    private void OnStatusChanged(object? sender, EventArgs e)
        => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _ = LoadAsync());

    private static void RestartApplication()
    {
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose() => _statusNotifier.StatusChanged -= OnStatusChanged;
}
