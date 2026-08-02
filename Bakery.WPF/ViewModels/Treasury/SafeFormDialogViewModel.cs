using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class SafeFormDialogViewModel : ViewModelBase
{
    private readonly ISafeService _safeService;
    private readonly IMessageService _messageService;
    private SafeManagementDto? _existingSafe;

    public SafeFormDialogViewModel(ISafeService safeService, IMessageService messageService)
    {
        _safeService = safeService;
        _messageService = messageService;
        Title = "إضافة خزنة جديدة";
        ArabicName = string.Empty;
        IsActive = true;
        IsEditMode = false;
        IsSystem = false;
        Type = SafeType.Normal;
    }

    public void InitializeForEdit(SafeManagementDto existingSafe)
    {
        _existingSafe = existingSafe;
        Title = "تعديل بيانات الخزنة";
        ArabicName = existingSafe.ArabicName ?? existingSafe.Name;
        IsActive = existingSafe.IsActive;
        IsEditMode = true;
        IsSystem = existingSafe.IsSystem;
        Type = existingSafe.Type;
    }

    [ObservableProperty] private string arabicName;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private bool isEditMode;
    [ObservableProperty] private bool isSystem;
    [ObservableProperty] private SafeType type;

    public string TypeDisplayName => Type switch
    {
        SafeType.Main => "ثابتة - رئيسية",
        SafeType.Private => "ثابتة - خاصة",
        SafeType.Daily => "ثابتة - اليوم",
        SafeType.Normal => "عادية",
        _ => "عادية"
    };

    public event EventHandler<bool>? RequestClose;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(ArabicName))
        {
            _messageService.ShowError("اسم الخزنة مطلوب");
            return;
        }

        try
        {
            if (IsEditMode)
            {
                var request = new UpdateSafeRequest(_existingSafe!.Id, ArabicName, IsActive);
                await _safeService.UpdateSafeAsync(request);
            }
            else
            {
                var request = new CreateSafeRequest(ArabicName);
                await _safeService.CreateSafeAsync(request);
            }

            RequestClose?.Invoke(this, true);
        }
        catch (ValidationException ex)
        {
            _messageService.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Save safe"));
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, false);
}
