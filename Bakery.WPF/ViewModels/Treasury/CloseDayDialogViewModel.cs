using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class CloseDayDialogViewModel : ViewModelBase
{
    private readonly IWorkingDayService _workingDayService;
    private readonly IMessageService _messageService;
    private readonly INavigationService _navigationService;
    private readonly Guid _operationId = Guid.NewGuid();

    public CloseDayDialogViewModel(
        IWorkingDayService workingDayService,
        IMessageService messageService,
        INavigationService navigationService,
        IPermissionService permissionService)
    {
        _workingDayService = workingDayService;
        _messageService = messageService;
        _navigationService = navigationService;
        CanOverrideCloseBlockers = permissionService.HasPermission(PermissionKeys.WorkingDayOverrideCloseBlockers);
        Title = Loc.CloseWorkingDay;
    }

    [ObservableProperty] private decimal currentCash;
    [ObservableProperty] private decimal transferredToMainSafe;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private bool adminOverride;
    [ObservableProperty] private string? overrideReason;
    [ObservableProperty] private WorkingDaySummaryDto? summary;
    [ObservableProperty] private IReadOnlyList<WorkingDayBlockerDto> blockers = [];
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isSubmitting;

    public decimal TotalExpenses => (Summary?.Expenses ?? 0) + (Summary?.Wages ?? 0);
    public decimal CarryOverBalance => CurrentCash - TransferredToMainSafe;
    public bool HasBlockers => Blockers.Count > 0;
    public bool CanOverrideCloseBlockers { get; }
    public string BlockersMessage => HasBlockers
        ? "لا يمكن إنهاء يوم العمل للأسباب التالية:"
        : string.Empty;
    public string SubmitButtonText => IsSubmitting ? "جاري إنهاء يوم العمل..." : "تأكيد إنهاء اليوم";

    public event EventHandler<bool>? RequestClose;

    partial void OnCurrentCashChanged(decimal value) => OnPropertyChanged(nameof(CarryOverBalance));
    partial void OnTransferredToMainSafeChanged(decimal value) => OnPropertyChanged(nameof(CarryOverBalance));
    partial void OnSummaryChanged(WorkingDaySummaryDto? value)
    {
        OnPropertyChanged(nameof(TotalExpenses));
        CloseDayCommand.NotifyCanExecuteChanged();
    }
    partial void OnBlockersChanged(IReadOnlyList<WorkingDayBlockerDto> value)
    {
        OnPropertyChanged(nameof(HasBlockers));
        OnPropertyChanged(nameof(BlockersMessage));
        CloseDayCommand.NotifyCanExecuteChanged();
    }
    partial void OnAdminOverrideChanged(bool value)
    {
        if (value && !CanOverrideCloseBlockers)
        {
            AdminOverride = false;
            OverrideReason = null;
        }
        CloseDayCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsLoadingChanged(bool value) => CloseDayCommand.NotifyCanExecuteChanged();
    partial void OnIsSubmittingChanged(bool value)
    {
        OnPropertyChanged(nameof(SubmitButtonText));
        CloseDayCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        try
        {
            var readiness = await _workingDayService.GetEndOfDayReadinessAsync();
            ApplyReadiness(readiness, resetAllocation: true);
            if (!string.IsNullOrWhiteSpace(readiness.ErrorMessage))
                _messageService.ShowError(readiness.ErrorMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCloseDay))]
    private async Task CloseDayAsync()
    {
        if (IsSubmitting || Summary is null) return;

        if (CurrentCash < 0)
        {
            _messageService.ShowError("النقد الحالي يجب أن يكون موجبا.");
            return;
        }

        if (TransferredToMainSafe < 0)
        {
            _messageService.ShowError("المبلغ المرحل يجب أن يكون موجبا.");
            return;
        }

        if (TransferredToMainSafe > CurrentCash)
        {
            _messageService.ShowError("لا يمكن ترحيل مبلغ أكبر من النقد الحالي.");
            return;
        }

        var expectedWorkingDayId = Summary.WorkingDayId;
        IsSubmitting = true;
        try
        {
            var result = await _workingDayService.EndCurrentDayAndOpenNextAsync(new CloseWorkingDayRequest(
                TransferredToMainSafe,
                CarryOverBalance,
                Notes,
                AdminOverride,
                OverrideReason,
                expectedWorkingDayId,
                _operationId));

            if (!result.Succeeded)
            {
                if (result.Blockers is { Count: > 0 })
                    Blockers = result.Blockers;

                var readiness = await _workingDayService.GetEndOfDayReadinessAsync();
                if (readiness.Summary is not null && readiness.Summary.WorkingDayId != expectedWorkingDayId)
                {
                    _messageService.ShowError(result.ErrorMessage ?? "تم تغيير يوم العمل النشط. تم تحديث لوحة التحكم.");
                    RequestClose?.Invoke(this, true);
                    return;
                }

                ApplyReadiness(readiness, resetAllocation: true);
                _messageService.ShowError(result.ErrorMessage ?? Loc.NoOpenWorkingDay);
                return;
            }

            _messageService.ShowInfo($"تم إنهاء يوم العمل وفتح يوم جديد بتاريخ {result.Summary?.BusinessDate:dd/MM/yyyy}");
            RequestClose?.Invoke(this, true);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private bool CanCloseDay()
        => !IsLoading && !IsSubmitting && Summary is not null &&
            (!HasBlockers || (CanOverrideCloseBlockers && AdminOverride));

    [RelayCommand]
    private async Task OpenBlockerAsync(WorkingDayBlockerDto? blocker)
    {
        if (blocker is null || string.IsNullOrWhiteSpace(blocker.ActionLabel)) return;

        RequestClose?.Invoke(this, false);
        switch (blocker.Kind)
        {
            case WorkingDayBlockerKind.StockCount:
                _navigationService.NavigateTo<StockCountViewModel>();
                break;
            case WorkingDayBlockerKind.ProductionOrder:
                _navigationService.NavigateTo<ProductionViewModel>();
                break;
            case WorkingDayBlockerKind.SaleInvoice:
                _navigationService.NavigateTo<SalesViewModel>();
                break;
            case WorkingDayBlockerKind.PurchaseInvoice:
                var purchases = _navigationService.NavigateTo<PurchasesViewModel>();
                if (blocker.EntityId.HasValue)
                    await purchases.ShowBlockingDraftAsync(blocker.EntityId.Value);
                break;
            case WorkingDayBlockerKind.TreasuryMovement:
            case WorkingDayBlockerKind.FinancialIntegrity:
                _navigationService.NavigateTo<TreasuryViewModel>();
                break;
        }
    }

    private void ApplyReadiness(WorkingDayCloseReadinessDto readiness, bool resetAllocation)
    {
        Summary = readiness.Summary;
        Blockers = readiness.Blockers;
        if (!CanOverrideCloseBlockers)
        {
            AdminOverride = false;
            OverrideReason = null;
        }
        if (!resetAllocation) return;

        CurrentCash = Summary?.DailySafeBalance ?? 0;
        TransferredToMainSafe = CurrentCash;
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, false);
    }
}
