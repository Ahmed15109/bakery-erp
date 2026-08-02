using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakery.Application.Interfaces;
using Bakery.WPF.Services;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace Bakery.WPF.ViewModels;

public sealed partial class ReverseTransactionDialogViewModel : ViewModelBase
{
    private readonly ISafeService _safeService;
    private readonly IMessageService _messageService;

    public ReverseTransactionDialogViewModel(
        ISafeService safeService,
        IMessageService messageService)
    {
        _safeService = safeService;
        _messageService = messageService;
        Title = "إلغاء المعاملة المالية";
    }

    [ObservableProperty] private int transactionId;
    [ObservableProperty] private string transactionNumber = string.Empty;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string originalReason = string.Empty;
    [ObservableProperty] private string reverseReason = string.Empty;
    [ObservableProperty] private string createdBy = string.Empty;
    [ObservableProperty] private DateTime date;

    public event EventHandler<bool>? RequestClose;

    public void Initialize(int id, string number, decimal amt, string reason, string creator, DateTime dt)
    {
        TransactionId = id;
        TransactionNumber = number;
        Amount = amt;
        OriginalReason = reason;
        CreatedBy = creator;
        Date = dt;
        ReverseReason = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(ReverseReason))
        {
            _messageService.ShowError("يجب إدخال سبب الإلغاء");
            return;
        }

        try
        {
            var req = new Bakery.Application.DTOs.Accounting.ReverseTransactionRequest(
                TransactionId,
                ReverseReason
            );

            bool success = await _safeService.ReverseManualTransactionAsync(req);
            if (success)
            {
                RequestClose?.Invoke(this, true);
            }
            else
            {
                _messageService.ShowError("فشل إلغاء المعاملة. تأكد من فتح يوم العمل.");
            }
        }
        catch (ValidationException ex)
        {
            _messageService.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Reverse treasury transaction"));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, false);
    }
}
