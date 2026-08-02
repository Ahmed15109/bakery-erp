using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Bakery.WPF.ViewModels;

public sealed partial class AuditLogViewModel : ViewModelBase
{
    private readonly IAuditQueryService _auditQueryService;
    private readonly IPermissionService _permissionService;
    private readonly IMessageService _messageService;
    private readonly IExceptionTranslator _exceptionTranslator;
    private int _refreshVersion;

    public AuditLogViewModel(
        IAuditQueryService auditQueryService,
        IPermissionService permissionService,
        IMessageService messageService,
        IExceptionTranslator exceptionTranslator)
    {
        _auditQueryService = auditQueryService;
        _permissionService = permissionService;
        _messageService = messageService;
        _exceptionTranslator = exceptionTranslator;
        Title = "سجل التدقيق";
        _ = RefreshAsync();
    }

    public ObservableCollection<AuditLogDto> Entries { get; } = [];

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private DateTime? fromDate = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime? toDate = DateTime.Today;

    partial void OnSearchTextChanged(string value) => _ = DebouncedRefreshAsync();
    partial void OnFromDateChanged(DateTime? value) => _ = RefreshAsync();
    partial void OnToDateChanged(DateTime? value) => _ = RefreshAsync();

    private async Task DebouncedRefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        await Task.Delay(300);
        if (version == _refreshVersion) await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        try
        {
            IsBusy = true;
            var request = new AuditSearchRequest(
                null,
                FromDate?.Date.ToUniversalTime(),
                ToDate?.Date.AddDays(1).AddTicks(-1).ToUniversalTime(),
                Take: 1000);

            var rawEntries = await _auditQueryService.SearchAsync(request);
            if (version != _refreshVersion) return;

            var localizedList = rawEntries.Select(LocalizeEntry).ToList();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim();
                localizedList = localizedList.Where(e =>
                    (e.UserName != null && e.UserName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (e.Action != null && e.Action.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (e.EntityName != null && e.EntityName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (e.EntityId.HasValue && e.EntityId.Value.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (e.OldValues != null && e.OldValues.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (e.NewValues != null && e.NewValues.Contains(term, StringComparison.OrdinalIgnoreCase))
                ).ToList();
            }

            Entries.Clear();
            foreach (var entry in localizedList) Entries.Add(entry);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = $"audit-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await using var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
            await writer.WriteLineAsync("التاريخ والوقت,المستخدم,العملية,الكيان,الرقم,اسم الجهاز,عنوان IP,القيمة السابقة,القيمة الجديدة / النتيجة");
            foreach (var row in Entries)
            {
                var fields = new[]
                {
                    row.OccurredAt.ToString("yyyy/MM/dd HH:mm:ss"),
                    row.UserName,
                    row.Action,
                    row.EntityName,
                    row.EntityId?.ToString(),
                    row.MachineName,
                    row.IPAddress,
                    row.OldValues,
                    row.NewValues
                };
                await writer.WriteLineAsync(string.Join(',', fields.Select(EscapeCsv)));
            }
            _messageService.ShowInfo("تم تصدير سجل التدقيق بنجاح.");
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    private static AuditLogDto LocalizeEntry(AuditLogDto raw)
    {
        return raw with
        {
            Action = Loc.LocalizeAuditAction(raw.Action),
            EntityName = Loc.LocalizeAuditEntity(raw.EntityName),
            OldValues = Loc.LocalizeAuditValue(raw.OldValues),
            NewValues = Loc.LocalizeAuditValue(raw.NewValues)
        };
    }

    private static string EscapeCsv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private bool CanExport() => _permissionService.HasPermission(PermissionKeys.AuditExport);
}
