using System;
using System.Linq;
using System.Threading.Tasks;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public enum MismatchResult
{
    Cancel,
    ContinueWithCurrent,
    SwitchToOriginal
}

public sealed partial class SafeMismatchDialogViewModel : ViewModelBase
{
    private readonly ISafeService _safeService;

    [ObservableProperty] private string originalSafeName = string.Empty;
    [ObservableProperty] private string currentSafeName = string.Empty;
    [ObservableProperty] private bool canSwitchToOriginal;
    
    public MismatchResult Result { get; private set; } = MismatchResult.Cancel;
    
    public event EventHandler<bool>? RequestClose;

    public SafeMismatchDialogViewModel(ISafeService safeService)
    {
        _safeService = safeService;
        Title = "تنبيه تعارض الخزنة";
    }

    public async Task InitializeAsync(string originalSafeName, string currentSafeName, int originalSafeId)
    {
        OriginalSafeName = originalSafeName;
        CurrentSafeName = currentSafeName;
        
        try
        {
            var accessibleSafes = await _safeService.ListSafesAsync();
            CanSwitchToOriginal = accessibleSafes.Any(s => s.Id == originalSafeId);
        }
        catch
        {
            CanSwitchToOriginal = false;
        }
    }

    [RelayCommand]
    private void ContinueWithCurrent()
    {
        Result = MismatchResult.ContinueWithCurrent;
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void SwitchToOriginal()
    {
        Result = MismatchResult.SwitchToOriginal;
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Abort()
    {
        Result = MismatchResult.Cancel;
        RequestClose?.Invoke(this, false);
    }
}
