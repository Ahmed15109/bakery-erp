using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakery.WPF.Services;

namespace Bakery.WPF.ViewModels;

public sealed partial class TreasuryTransactionDialogViewModel : ViewModelBase
{
    private readonly ISafeService _safeService;
    private readonly IMessageService _messageService;
    private int _lockedTreasuryId;
    private readonly string _idempotencyKey = Guid.NewGuid().ToString("N");

    public TreasuryTransactionDialogViewModel(
        ISafeService safeService, 
        IMessageService messageService)
    {
        _safeService = safeService;
        _messageService = messageService;
        Title = "معاملة مالية";
        Safes = [];
    }

    public async Task InitializeAsync(int treasuryId, bool isDeposit)
    {
        if (treasuryId <= 0) throw new ArgumentOutOfRangeException(nameof(treasuryId));

        _lockedTreasuryId = treasuryId;
        _isLoadingSafes = true;
        try
        {
            IsDeposit = isDeposit;
            CanChangeSafe = false;
            await LoadSafesAsync(treasuryId);
        }
        finally
        {
            _isLoadingSafes = false;
        }
    }

    [ObservableProperty] private bool canChangeSafe;

    public ObservableCollection<SafeDto> Safes { get; }
    
    public IEnumerable<ManualMovementReason> PredefinedReasons { get; } = Enum.GetValues(typeof(ManualMovementReason)).Cast<ManualMovementReason>();

    [ObservableProperty] private int selectedSafeId;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private ManualMovementReason? selectedReason;
    [ObservableProperty] private string referenceNumber = string.Empty;
    [ObservableProperty] private string attachmentPath = string.Empty;
    [ObservableProperty] private bool isOtherSelected;
    [ObservableProperty] private bool isDeposit = true;
    
    [ObservableProperty] private decimal currentSafeBalance;
    [ObservableProperty] private string balanceWarning = string.Empty;
    [ObservableProperty] private bool isInsufficientFunds;
    [ObservableProperty] private bool isSaving;

    public bool CanSubmit => !IsSaving && !IsInsufficientFunds;

    partial void OnIsSavingChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnIsInsufficientFundsChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));

    public event EventHandler<bool>? RequestClose;

    private bool _isLoadingSafes;

    private async Task LoadSafesAsync(int treasuryId)
    {
        _isLoadingSafes = true;
        try
        {
            var safes = IsDeposit
                ? await _safeService.ListSafesForDepositAsync()
                : await _safeService.ListSafesForWithdrawAsync();

            Safes.Clear();
            foreach (var safe in safes)
            {
                Safes.Add(safe);
            }

            if (!Safes.Any(safe => safe.Id == treasuryId))
            {
                throw new UnauthorizedAccessException("الخزينة المحددة غير متاحة لهذه العملية.");
            }

            SelectedSafeId = treasuryId;
            await UpdateBalanceInfoAsync();
        }
        finally
        {
            _isLoadingSafes = false;
        }
    }

    partial void OnSelectedReasonChanged(ManualMovementReason? value)
    {
        IsOtherSelected = value == ManualMovementReason.Other;
        if (!IsOtherSelected)
        {
            Description = string.Empty;
        }
    }

    partial void OnSelectedSafeIdChanged(int value)
    {
        if (value <= 0)
        {
            CurrentSafeBalance = 0;
            UpdateBalanceInfo();
            return;
        }
        if (!_isLoadingSafes)
        {
            _ = UpdateBalanceInfoAsync();
        }
    }

    partial void OnAmountChanged(decimal value) => UpdateBalanceInfo();
    partial void OnIsDepositChanged(bool value)
    {
        UpdateBalanceInfo();
    }

    private async Task UpdateBalanceInfoAsync()
    {
        if (SelectedSafeId <= 0) return;
        CurrentSafeBalance = await _safeService.GetBalanceAsync(SelectedSafeId);
        UpdateBalanceInfo();
    }

    private void UpdateBalanceInfo()
    {
        if (!IsDeposit && Amount > CurrentSafeBalance)
        {
            IsInsufficientFunds = true;
            decimal deficit = Amount - CurrentSafeBalance;
            BalanceWarning = $"رصيد غير كافٍ! العجز: {deficit:N2} ج.م";
        }
        else
        {
            IsInsufficientFunds = false;
            BalanceWarning = string.Empty;
        }
    }

    [RelayCommand]
    private void BrowseAttachment()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "اختر ملف المرفق",
            Filter = "All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            AttachmentPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving) return;
        if (SelectedSafeId != _lockedTreasuryId)
        {
            _messageService.ShowError("تم تغيير الخزينة المحددة. أغلق النافذة وحاول مرة أخرى.");
            return;
        }
        if (Amount <= 0) { _messageService.ShowError("المبلغ يجب أن يكون أكبر من صفر"); return; }
        if (SelectedReason == null) { _messageService.ShowError("السبب مطلوب"); return; }
        if (SelectedReason == ManualMovementReason.Other && string.IsNullOrWhiteSpace(Description)) 
        { 
            _messageService.ShowError("البيان مطلوب عند اختيار 'أخرى'"); 
            return; 
        }

        IsSaving = true;
        try
        {
            var finalDescription = SelectedReason == ManualMovementReason.Other ? Description : GetLocalizedReason(SelectedReason.Value);

            var req = new ManualCashTransactionRequest(
                SelectedSafeId,
                Amount,
                SelectedReason.Value,
                finalDescription,
                ReferenceNumber,
                AttachmentPath,
                _idempotencyKey
            );

            bool success;
            if (IsDeposit)
            {
                success = await _safeService.ManualDepositAsync(req);
            }
            else
            {
                success = await _safeService.ManualWithdrawalAsync(req);
            }

            if (success) RequestClose?.Invoke(this, true);
            else _messageService.ShowError("فشل تنفيذ المعاملة. تأكد من فتح يوم العمل.");
        }
        catch (ValidationException ex)
        {
            _messageService.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Save treasury transaction"));
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string GetLocalizedReason(ManualMovementReason reason)
    {
        return reason switch
        {
            ManualMovementReason.OwnerCapital => "رأس مال المالك",
            ManualMovementReason.OwnerWithdrawal => "مسحوبات المالك",
            ManualMovementReason.BankDeposit => "إيداع بنكي",
            ManualMovementReason.BankWithdrawal => "سحب بنكي",
            ManualMovementReason.CashAdjustment => "تسوية نقدية",
            ManualMovementReason.TransferCorrection => "تصحيح تحويل",
            ManualMovementReason.Emergency => "حركة نقدية طارئة",
            _ => reason.ToString()
        };
    }

    [RelayCommand] private void Cancel() => RequestClose?.Invoke(this, false);
}
