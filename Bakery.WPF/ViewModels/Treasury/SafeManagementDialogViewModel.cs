using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class SafeManagementDialogViewModel : ViewModelBase
{
    private readonly ISafeService _safeService;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;

    public SafeManagementDialogViewModel(ISafeService safeService, IMessageService messageService, IDialogService dialogService)
    {
        _safeService = safeService;
        _messageService = messageService;
        _dialogService = dialogService;
        Title = "إدارة الخزن";
        Safes = [];
    }

    public async Task InitializeAsync()
    {
        await LoadSafesAsync();
    }

    public ObservableCollection<SafeManagementDto> Safes { get; }

    [ObservableProperty] private SafeManagementDto? selectedSafe;

    public event EventHandler<bool>? RequestClose;

    public async Task LoadSafesAsync()
    {
        Safes.Clear();
        var list = await _safeService.ListAllSafesForManagementAsync();
        foreach (var safe in list)
        {
            Safes.Add(safe);
        }
    }

    [RelayCommand]
    private async Task AddSafeAsync()
    {
        var result = await _dialogService.ShowDialogAsync<SafeFormDialogViewModel>();
        if (result.Result == true)
        {
            await LoadSafesAsync();
        }
    }

    [RelayCommand]
    private async Task EditSafeAsync()
    {
        if (SelectedSafe == null) return;

        var result = await _dialogService.ShowDialogAsync<SafeFormDialogViewModel>(vm =>
        {
            vm.InitializeForEdit(SelectedSafe);
            return Task.CompletedTask;
        });

        if (result.Result == true)
        {
            await LoadSafesAsync();
        }
    }

    [RelayCommand]
    private async Task DeactivateSafeAsync()
    {
        if (SelectedSafe == null) return;
        if (SelectedSafe.IsSystem)
        {
            _messageService.ShowError("لا يمكن تعديل أو تعطيل خزنة نظام.");
            return;
        }

        if (!_messageService.Confirm($"هل أنت متأكد من تعطيل الخزنة '{SelectedSafe.DisplayName}'؟")) return;

        try
        {
            await _safeService.DeactivateSafeAsync(SelectedSafe.Id);
            _messageService.ShowInfo("تم تعطيل الخزنة بنجاح.");
            await LoadSafesAsync();
        }
        catch (ValidationException ex)
        {
            _messageService.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Disable safe"));
        }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, true);
}
