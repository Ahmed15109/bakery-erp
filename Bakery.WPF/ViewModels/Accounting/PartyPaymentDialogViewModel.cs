using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class PartyPaymentDialogViewModel : ViewModelBase
{
    private readonly IPartyPaymentService _paymentService;
    private readonly ISafeService _safeService;
    private readonly IMessageService _messageService;
    private readonly string _idempotencyKey = Guid.NewGuid().ToString("N");

    [ObservableProperty] private PartySummaryDto? party;
    [ObservableProperty] private string operationTypeDisplay = string.Empty;
    [ObservableProperty] private int selectedSafeId;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private DateTime date = DateTime.Now;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private bool isSaving;
    [ObservableProperty] private System.Windows.Visibility mixedComboVisibility = System.Windows.Visibility.Collapsed;
    [ObservableProperty] private System.Windows.Visibility operationTextBoxVisibility = System.Windows.Visibility.Visible;
    [ObservableProperty] private int selectedOperationIndex = 0; 

    public ObservableCollection<SafeDto> Safes { get; } = new();

    public event EventHandler<bool>? RequestClose;

    private readonly ISafeContext _safeContext;

    [ObservableProperty] private bool canChangeSafe;

    public PartyPaymentDialogViewModel(
        IPartyPaymentService paymentService,
        ISafeService safeService,
        IMessageService messageService,
        ISafeContext safeContext)
    {
        _paymentService = paymentService;
        _safeService = safeService;
        _messageService = messageService;
        _safeContext = safeContext;
        
        CanChangeSafe = !_safeContext.CurrentSafeId.HasValue;
    }

    private int _partyId;

    public async Task InitializeAsync(int partyId, PartySummaryDto partyDto)
    {
        _partyId = partyId;
        Party = partyDto;
        Title = "معاملة مالية";
        
        if (Party.Type == Bakery.Domain.Enums.PartyType.Mixed)
        {
            MixedComboVisibility = System.Windows.Visibility.Visible;
            OperationTextBoxVisibility = System.Windows.Visibility.Collapsed;
            SelectedOperationIndex = 0; 
            OperationTypeDisplay = "عميل ومورد";
        }
        else
        {
            MixedComboVisibility = System.Windows.Visibility.Collapsed;
            OperationTextBoxVisibility = System.Windows.Visibility.Visible;
            SelectedOperationIndex = Party.Type != Bakery.Domain.Enums.PartyType.Supplier ? 0 : 1;
            OperationTypeDisplay = SelectedOperationIndex == 0 ? "استلام من العميل" : "سداد للمورد";
        }

        var list = await _safeService.ListSafesAsync();
        foreach (var safe in list) Safes.Add(safe);

        if (_safeContext.CurrentSafeId.HasValue && Safes.Any(s => s.Id == _safeContext.CurrentSafeId.Value))
        {
            SelectedSafeId = _safeContext.CurrentSafeId.Value;
        }
        else
        {
            SelectedSafeId = (await _safeService.GetDefaultCashSafeAsync()).Id;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving) return;

        if (Amount <= 0)
        {
            _messageService.ShowError("المبلغ يجب أن يكون أكبر من صفر");
            return;
        }

        if (Party == null) return;

        IsSaving = true;
        try
        {
            var result = await _paymentService.ProcessPaymentAsync(
                _partyId,
                SelectedSafeId,
                Amount,
                Description,
                SelectedOperationIndex == 0,
                _idempotencyKey
            );

            if (result.Succeeded)
            {
                RequestClose?.Invoke(this, true);
            }
            else
            {
                _messageService.ShowError(result.ErrorMessage ?? "فشل تنفيذ العملية");
            }
        }
        catch (System.ComponentModel.DataAnnotations.ValidationException ex)
        {
            _messageService.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Save party payment"));
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, false);
}
