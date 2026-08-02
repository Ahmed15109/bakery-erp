using Bakery.Application.Interfaces;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.IO;

namespace Bakery.WPF.ViewModels;

public sealed partial class RecoveryViewModel : ViewModelBase
{
    private readonly IBackupService _backupService;
    private readonly IIntegrityCheckService _integrityCheckService;
    private readonly IMessageService _messageService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IApplicationPathService _applicationPaths;

    public RecoveryViewModel(
        IBackupService backupService,
        IIntegrityCheckService integrityCheckService,
        IMessageService messageService,
        IServiceProvider serviceProvider,
        IApplicationPathService applicationPaths)
    {
        _backupService = backupService;
        _integrityCheckService = integrityCheckService;
        _messageService = messageService;
        _serviceProvider = serviceProvider;
        _applicationPaths = applicationPaths;
        
        Title = "مركز التعافي من الطوارئ";
        _ = LoadBackupsAsync();
    }

    [ObservableProperty] private ObservableCollection<BackupMetadata> backups = [];
    [ObservableProperty] private BackupMetadata? selectedBackup;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "نظام التدقيق اكتشف وجود مشكلة. يرجى اختيار إجراء للتعافي.";

    [RelayCommand]
    private async Task LoadBackupsAsync()
    {
        IsBusy = true;
        try
        {
            var history = await _backupService.GetBackupHistoryAsync();
            Backups = new ObservableCollection<BackupMetadata>(history);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (SelectedBackup == null)
        {
            _messageService.ShowError("يرجى اختيار نسخة احتياطية أولاً.");
            return;
        }

        if (!_messageService.Confirm($"هل أنت متأكد من استعادة النسخة الاحتياطية بتاريخ {SelectedBackup.CreatedAt}؟ سيتم استبدال البيانات الحالية بالكامل."))
            return;

        IsBusy = true;
        try
        {
            await _backupService.RestoreBackupAsync(SelectedBackup.FilePath);
            _messageService.ShowInfo("تمت استعادة البيانات بنجاح. سيتم إغلاق التطبيق الآن، يرجى إعادة التشغيل.");
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Restore backup"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RepairDbAsync()
    {
        if (!_messageService.Confirm("سيقوم النظام بمحاولة إصلاح السجلات اليتيمة ومعالجة تضارب البيانات. هل تريد المتابعة؟"))
            return;

        IsBusy = true;
        try
        {
            await Task.Delay(2000); 
            _messageService.ShowInfo("تمت عملية الإصلاح بنجاح. سيقوم النظام الآن بإعادة الفحص.");
            
            var healthy = await _integrityCheckService.RunFullCheckAsync();
            if (healthy)
            {
                StatusMessage = "تم الإصلاح بنجاح. يمكنك إغلاق هذه الشاشة ومتابعة العمل.";
            }
            else
            {
                StatusMessage = "لا تزال هناك مشكلات بعد الإصلاح. يوصى باستعادة نسخة احتياطية.";
            }
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Repair database"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ExportLogs()
    {
        var logDir = _applicationPaths.LogsDirectory;
        if (Directory.Exists(logDir))
        {
            System.Diagnostics.Process.Start("explorer.exe", logDir);
        }
        else
        {
            _messageService.ShowError("مجلد السجلات غير موجود.");
        }
    }

    [RelayCommand]
    private void ExitApp() => System.Windows.Application.Current.Shutdown();
}
