using Bakery.Application.Interfaces;
using Bakery.Application.DTOs;
using Bakery.Application.Security;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Bakery.WPF.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISystemResetService _resetService;
    private readonly IWorkingDayService _workingDayService;
    private readonly IPermissionService _permissionService;
    private readonly IUserSessionService _userSessionService;
    private readonly IMessageService _messageService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IOwnerResetAuthorizationPrompt _ownerResetPrompt;
    private readonly IOperationalContextRefreshNotifier _refreshNotifier;
    private readonly IWorkingDayReopenResolutionService? _reopenResolutionService;

    public SettingsViewModel(
        ISystemResetService resetService,
        IWorkingDayService workingDayService,
        IPermissionService permissionService,
        IUserSessionService userSessionService,
        IMessageService messageService,
        INavigationService navigationService,
        IDialogService dialogService,
        IOwnerResetAuthorizationPrompt ownerResetPrompt,
        IOperationalContextRefreshNotifier refreshNotifier,
        IWorkingDayReopenResolutionService? reopenResolutionService = null)
    {
        _resetService = resetService;
        _workingDayService = workingDayService;
        _permissionService = permissionService;
        _userSessionService = userSessionService;
        _messageService = messageService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _ownerResetPrompt = ownerResetPrompt;
        _refreshNotifier = refreshNotifier;
        _reopenResolutionService = reopenResolutionService;
        Title = Loc.Settings;
        CanUseReopenPermission = _permissionService.HasPermission(PermissionKeys.WorkingDayReopen);
        InitializationTask = LoadWorkingDayAsync();
    }

    [ObservableProperty] private bool isResetting;
    [ObservableProperty] private string confirmationText = string.Empty;
    [ObservableProperty] private bool isWorkingDayBusy;
    [ObservableProperty] private WorkingDaySummaryDto? workingDaySummary;
    [ObservableProperty] private WorkingDaySummaryDto? lastClosedWorkingDaySummary;
    [ObservableProperty] private bool isReopenEligible;
    [ObservableProperty] private string workingDayStatusText = "لا يوجد يوم عمل مفتوح";
    [ObservableProperty] private string workingDayBusinessDateText = "—";
    [ObservableProperty] private string lastClosedBusinessDateText = "لا يوجد يوم عمل مغلق متاح لإعادة الفتح";
    [ObservableProperty] private string workingDayClosedByText = "—";
    [ObservableProperty] private string workingDayClosedAtText = "—";
    [ObservableProperty] private string reopenEligibilityText = "لا يوجد يوم عمل مغلق متاح لإعادة الفتح";
    [ObservableProperty] private string reopenButtonText = "إعادة فتح آخر يوم عمل";
    [ObservableProperty] private int reopenBlockerCount;

    public Task InitializationTask { get; }
    public ObservableCollection<WorkingDayReopenBlockerDto> ReopenBlockers { get; } = [];
    public bool HasReopenBlockers => ReopenBlockerCount > 0;
    public bool CanUseReopenPermission { get; }
    public bool CanResetSystem =>
        _userSessionService.CurrentUser?.IsSuperAdmin == true &&
        _permissionService.HasPermission(PermissionKeys.SettingsResetSystem);

    private bool CanReopenLatestDay() =>
        !IsWorkingDayBusy &&
        CanUseReopenPermission &&
        IsReopenEligible &&
        LastClosedWorkingDaySummary?.Status == WorkingDayStatus.Closed;

    private bool CanFactoryReset() =>
        !IsResetting &&
        CanResetSystem &&
        string.Equals(ConfirmationText, "DELETE", StringComparison.Ordinal);

    partial void OnConfirmationTextChanged(string value) => FactoryResetCommand.NotifyCanExecuteChanged();
    partial void OnIsResettingChanged(bool value) => FactoryResetCommand.NotifyCanExecuteChanged();
    partial void OnIsWorkingDayBusyChanged(bool value) => ReopenWorkingDayCommand.NotifyCanExecuteChanged();
    partial void OnLastClosedWorkingDaySummaryChanged(WorkingDaySummaryDto? value) => ReopenWorkingDayCommand.NotifyCanExecuteChanged();
    partial void OnIsReopenEligibleChanged(bool value) => ReopenWorkingDayCommand.NotifyCanExecuteChanged();
    partial void OnReopenBlockerCountChanged(int value) => OnPropertyChanged(nameof(HasReopenBlockers));

    [RelayCommand]
    private async Task LoadWorkingDayAsync()
    {
        if (!_permissionService.HasAnyPermission(PermissionKeys.WorkingDayView, PermissionKeys.WorkingDayReopen))
        {
            WorkingDaySummary = null;
            LastClosedWorkingDaySummary = null;
            IsReopenEligible = false;
            WorkingDayStatusText = "لا توجد صلاحية لعرض يوم العمل";
            WorkingDayBusinessDateText = "—";
            LastClosedBusinessDateText = "لا يوجد يوم عمل مغلق متاح لإعادة الفتح";
            WorkingDayClosedByText = "—";
            WorkingDayClosedAtText = "—";
            ReopenEligibilityText = "ليست لديك صلاحية عرض أو إعادة فتح يوم العمل.";
            ReopenButtonText = "إعادة فتح آخر يوم عمل";
            ReopenBlockers.Clear();
            ReopenBlockerCount = 0;
            return;
        }

        try
        {
            IsWorkingDayBusy = true;
            var eligibility = await _workingDayService.GetReopenEligibilityAsync();
            var currentDay = eligibility.CurrentActiveDay;
            var lastClosedDay = eligibility.LastClosedDay;
            WorkingDaySummary = currentDay;
            LastClosedWorkingDaySummary = lastClosedDay;
            IsReopenEligible = eligibility.CanReopen;
            ReopenEligibilityText = eligibility.StatusMessage;
            ReopenBlockers.Clear();
            foreach (var blocker in eligibility.Blockers ?? []) ReopenBlockers.Add(blocker);
            ReopenBlockerCount = ReopenBlockers.Count;

            if (currentDay is null)
            {
                WorkingDayStatusText = "لا يوجد يوم عمل مفتوح";
                WorkingDayBusinessDateText = "—";
            }
            else
            {
                WorkingDayStatusText = currentDay.Status == WorkingDayStatus.Open ? "مفتوح" : "مغلق";
                WorkingDayBusinessDateText = currentDay.BusinessDate.ToString("dd/MM/yyyy");
            }

            if (lastClosedDay is null)
            {
                LastClosedBusinessDateText = "لا يوجد يوم عمل مغلق متاح لإعادة الفتح";
                WorkingDayClosedByText = "—";
                WorkingDayClosedAtText = "—";
                ReopenButtonText = "إعادة فتح آخر يوم عمل";
            }
            else
            {
                LastClosedBusinessDateText = lastClosedDay.BusinessDate.ToString("dd/MM/yyyy");
                WorkingDayClosedByText = string.IsNullOrWhiteSpace(lastClosedDay.LastClosedBy)
                    ? "—"
                    : lastClosedDay.LastClosedBy;
                WorkingDayClosedAtText = lastClosedDay.LastClosedAt.HasValue
                    ? lastClosedDay.LastClosedAt.Value.ToLocalTime().ToString("dd/MM/yyyy hh:mm tt")
                    : "—";
                ReopenButtonText = $"إعادة فتح يوم {lastClosedDay.BusinessDate:dd/MM/yyyy}";
            }
        }
        catch (Exception exception)
        {
            WorkingDaySummary = null;
            LastClosedWorkingDaySummary = null;
            IsReopenEligible = false;
            WorkingDayStatusText = "تعذر تحميل حالة يوم العمل";
            ReopenEligibilityText = "تعذر التحقق من إمكانية إعادة فتح آخر يوم عمل.";
            ReopenBlockers.Clear();
            ReopenBlockerCount = 0;
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(
                exception,
                "Load working day settings status"));
        }
        finally
        {
            IsWorkingDayBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResolveReopenBlockerAsync(WorkingDayReopenBlockerDto? blocker)
    {
        if (blocker is null || IsWorkingDayBusy) return;
        if (!blocker.CanResolve || blocker.ActionKind == WorkingDayReopenActionKind.None)
        {
            _messageService.ShowError(blocker.UnsupportedMessage ?? "لا يمكن التراجع عن هذه العملية تلقائياً");
            return;
        }
        if (_reopenResolutionService is null)
        {
            _messageService.ShowError("خدمة التراجع غير متاحة حالياً.");
            return;
        }

        var reason = await _messageService.ShowInputAsync(
            "سبب التراجع",
            $"أدخل سبباً باللغة العربية للتراجع عن {blocker.TypeLabel} رقم {blocker.RecordNumber}:");
        if (string.IsNullOrWhiteSpace(reason)) return;
        if (!_messageService.Confirm(
                $"سيتم تنفيذ «{blocker.ActionLabel}» على {blocker.TypeLabel} رقم {blocker.RecordNumber}.\n\n" +
                $"الآثار: {blocker.EffectSummary}\n\nهل تريد المتابعة؟"))
            return;

        try
        {
            IsWorkingDayBusy = true;
            var result = await _reopenResolutionService.ResolveAsync(new ResolveWorkingDayReopenBlockerRequest(
                blocker.Code,
                reason,
                Guid.NewGuid()));
            await LoadWorkingDayAsync();
            if (!result.Succeeded)
            {
                _messageService.ShowError(result.ErrorMessage ?? "تعذر التراجع عن العملية.");
                return;
            }

            await _refreshNotifier.RequestRefreshAsync();
            _messageService.ShowInfo(result.WasAlreadyResolved
                ? "تمت معالجة هذه العملية مسبقاً وتم تحديث القائمة."
                : $"تمت معالجة {blocker.TypeLabel} رقم {blocker.RecordNumber} وتحديث حالة إعادة الفتح.");
        }
        catch (Exception exception)
        {
            await LoadWorkingDayAsync();
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(
                exception,
                "Resolve working day reopen blocker"));
        }
        finally
        {
            IsWorkingDayBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenReopenBlockerAsync(WorkingDayReopenBlockerDto? blocker)
    {
        if (blocker is null) return;
        switch (blocker.Kind)
        {
            case WorkingDayReopenBlockerKind.SaleInvoice:
                var sales = _navigationService.NavigateTo<SalesViewModel>();
                await sales.ShowBlockingInvoiceAsync(blocker.EntityId);
                break;
            case WorkingDayReopenBlockerKind.PurchaseInvoice:
                var purchases = _navigationService.NavigateTo<PurchasesViewModel>();
                await purchases.ShowBlockingInvoiceAsync(blocker.EntityId);
                break;
            case WorkingDayReopenBlockerKind.ProductionOrder:
                var production = _navigationService.NavigateTo<ProductionViewModel>();
                await production.ShowBlockingOrderAsync(blocker.EntityId);
                break;
            case WorkingDayReopenBlockerKind.StockCount:
                _navigationService.NavigateTo<StockCountViewModel>();
                break;
            case WorkingDayReopenBlockerKind.InventoryAdjustment:
            case WorkingDayReopenBlockerKind.Waste:
                _navigationService.NavigateTo<InventoryMovementsViewModel>();
                break;
            case WorkingDayReopenBlockerKind.TreasuryTransaction:
            case WorkingDayReopenBlockerKind.PartyPayment:
            case WorkingDayReopenBlockerKind.Expense:
                _navigationService.NavigateTo<TreasuryViewModel>();
                break;
            case WorkingDayReopenBlockerKind.EmployeeWage:
            case WorkingDayReopenBlockerKind.Attendance:
            case WorkingDayReopenBlockerKind.EmployeeTransaction:
            case WorkingDayReopenBlockerKind.Payroll:
                _navigationService.NavigateTo<EmployeesViewModel>();
                break;
            case WorkingDayReopenBlockerKind.PartyLedger:
                _navigationService.NavigateTo<PartiesViewModel>();
                break;
            default:
                _messageService.ShowInfo("يرجى مراجعة سجل التدقيق وحالة أيام العمل.");
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanReopenLatestDay))]
    private async Task ReopenWorkingDayAsync()
    {
        var summary = LastClosedWorkingDaySummary;
        if (summary is null || summary.Status != WorkingDayStatus.Closed) return;

        var dialogResult = await _dialogService.ShowDialogAsync<ReopenWorkingDayDialogViewModel>(viewModel =>
        {
            viewModel.Initialize(summary.WorkingDayId, summary.BusinessDate);
            return Task.CompletedTask;
        });
        if (dialogResult.Result != true) return;

        try
        {
            IsWorkingDayBusy = true;
            var result = await _workingDayService.ReopenDayAsync(
                summary.WorkingDayId,
                dialogResult.ViewModel.Reason);
            if (!result.Succeeded)
            {
                _messageService.ShowError(result.ErrorMessage ?? "تعذر إعادة فتح يوم العمل.");
                await LoadWorkingDayAsync();
                return;
            }

            await LoadWorkingDayAsync();
            await _refreshNotifier.RequestRefreshAsync();
            _messageService.ShowInfo($"تمت إعادة فتح يوم العمل بتاريخ {summary.BusinessDate:dd/MM/yyyy} بنجاح.");
        }
        catch (Exception exception)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(
                exception,
                "Reopen working day from settings"));
        }
        finally
        {
            IsWorkingDayBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanFactoryReset))]
    private async Task FactoryResetAsync()
    {
        if (!string.Equals(ConfirmationText, "DELETE", StringComparison.Ordinal))
        {
            _messageService.ShowError("يرجى كتابة كلمة DELETE للتأكيد");
            return;
        }

        if (IsResetting || !CanResetSystem) return;

        if (!_messageService.Confirm(
                "تحذير نهائي: سيتم حذف جميع بيانات الفرع التشغيلية نهائياً بعد إنشاء نسخة احتياطية آمنة. لا يمكن التراجع عن هذه العملية. هل تريد المتابعة؟"))
        {
            return;
        }

        var authorization = await _ownerResetPrompt.RequestAuthorizationAsync();
        if (authorization is null) return;

        try
        {
            IsResetting = true;
            await _resetService.ResetTransactionalDataAsync(authorization);
            await _refreshNotifier.RequestRefreshAsync();
            _messageService.ShowInfo("تمت إعادة ضبط النظام بنجاح بعد إنشاء نسخة احتياطية آمنة والتحقق منها.");
            _navigationService.NavigateTo<DashboardViewModel>();
        }
        catch (UnauthorizedAccessException)
        {
            _messageService.ShowError("غير مصرح بتنفيذ إعادة ضبط النظام.");
        }
        catch (Exception)
        {
            _messageService.ShowError(
                "تعذر إعادة ضبط النظام. لم تُترك بيانات محذوفة جزئياً. تحقق من النسخة الاحتياطية وسجل النظام ثم أعد المحاولة.");
        }
        finally
        {
            IsResetting = false;
            ConfirmationText = string.Empty;
        }
    }
}
