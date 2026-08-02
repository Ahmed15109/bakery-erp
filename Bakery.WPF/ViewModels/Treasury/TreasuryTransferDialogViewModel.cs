using System.Collections.ObjectModel;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakery.WPF.Services;
using System.ComponentModel.DataAnnotations;

namespace Bakery.WPF.ViewModels;

public sealed partial class TreasuryTransferDialogViewModel : ViewModelBase
{
    private readonly ISafeService _safeService;
    private readonly IMessageService _messageService;
    private int _lockedSourceTreasuryId;
    private readonly string _idempotencyKey = Guid.NewGuid().ToString("N");

    public TreasuryTransferDialogViewModel(
        ISafeService safeService, 
        IMessageService messageService)
    {
        _safeService = safeService;
        _messageService = messageService;
        Title = "تحويل بين الخزن";
        SourceSafes = [];
        DestinationSafes = [];
    }

    public async Task InitializeAsync(int sourceTreasuryId)
    {
        if (sourceTreasuryId <= 0) throw new ArgumentOutOfRangeException(nameof(sourceTreasuryId));

        _lockedSourceTreasuryId = sourceTreasuryId;
        _isLoadingSafes = true;
        try
        {
            CanChangeSource = false;
            await LoadSafesAsync(sourceTreasuryId);
        }
        finally
        {
            _isLoadingSafes = false;
        }
    }

    [ObservableProperty] private bool canChangeSource;

    public ObservableCollection<SafeDto> SourceSafes { get; }
    public ObservableCollection<SafeDto> DestinationSafes { get; }

    [ObservableProperty] private int sourceSafeId;
    [ObservableProperty] private int destinationSafeId;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string notes = string.Empty;

    [ObservableProperty] private decimal sourceSafeBalance;
    [ObservableProperty] private string balanceWarning = string.Empty;
    [ObservableProperty] private bool isInsufficientFunds;
    [ObservableProperty] private bool isSaving;

    public bool CanSubmit => !IsSaving && !IsInsufficientFunds;

    partial void OnIsSavingChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnIsInsufficientFundsChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));

    public event EventHandler<bool>? RequestClose;

    private bool _isLoadingSafes;

    private async Task LoadSafesAsync(int sourceTreasuryId)
    {
        _isLoadingSafes = true;
        try
        {
            var sources = await _safeService.ListSafesForTransferSourceAsync();
            var dests = await _safeService.ListSafesForTransferDestAsync();

            SourceSafes.Clear();
            foreach (var safe in sources)
            {
                SourceSafes.Add(safe);
            }

            DestinationSafes.Clear();
            foreach (var safe in dests)
            {
                DestinationSafes.Add(safe);
            }

            if (!SourceSafes.Any(safe => safe.Id == sourceTreasuryId))
            {
                throw new UnauthorizedAccessException("الخزينة المحددة غير متاحة للتحويل منها.");
            }

            SourceSafeId = sourceTreasuryId;

            DestinationSafeId = DestinationSafes.FirstOrDefault(s => s.Id != SourceSafeId)?.Id 
                               ?? DestinationSafes.FirstOrDefault()?.Id 
                               ?? 0;

            await UpdateBalanceInfoAsync();
        }
        finally
        {
            _isLoadingSafes = false;
        }
    }

    partial void OnSourceSafeIdChanged(int value)
    {
        if (value <= 0)
        {
            SourceSafeBalance = 0;
            UpdateBalanceInfo();
            return;
        }
        if (!_isLoadingSafes)
        {
            _ = UpdateBalanceInfoAsync();
        }
    }

    partial void OnAmountChanged(decimal value) => UpdateBalanceInfo();

    private async Task UpdateBalanceInfoAsync()
    {
        if (SourceSafeId <= 0) return;
        SourceSafeBalance = await _safeService.GetBalanceAsync(SourceSafeId);
        UpdateBalanceInfo();
    }

    private void UpdateBalanceInfo()
    {
        if (Amount > SourceSafeBalance)
        {
            IsInsufficientFunds = true;
            decimal deficit = Amount - SourceSafeBalance;
            BalanceWarning = $"رصيد غير كافٍ! العجز: {deficit:N2} ج.م";
        }
        else
        {
            IsInsufficientFunds = false;
            BalanceWarning = string.Empty;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving) return;
        if (SourceSafeId != _lockedSourceTreasuryId)
        {
            _messageService.ShowError("تم تغيير خزينة المصدر. أغلق النافذة وحاول مرة أخرى.");
            return;
        }
        if (Amount <= 0) { _messageService.ShowError("المبلغ يجب أن يكون أكبر من صفر"); return; }
        if (SourceSafeId == DestinationSafeId) { _messageService.ShowError("لا يمكن التحويل لنفس الخزنة"); return; }
        
        IsSaving = true;
        try
        {
            bool success = await _safeService.TransferAsync(
                SourceSafeId, DestinationSafeId, Amount, Notes, _idempotencyKey);
            if (success) RequestClose?.Invoke(this, true);
            else _messageService.ShowError("فشل تنفيذ التحويل. تأكد من فتح يوم العمل.");
        }
        catch (ValidationException ex)
        {
            _messageService.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Save treasury transfer"));
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand] private void Cancel() => RequestClose?.Invoke(this, false);
}
