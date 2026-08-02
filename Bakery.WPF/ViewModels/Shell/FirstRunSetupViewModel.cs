using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class FirstRunSetupViewModel : ViewModelBase
{
    private readonly IFirstRunSetupService _setupService;
    private int _submissionInProgress;

    public FirstRunSetupViewModel(IFirstRunSetupService setupService)
    {
        _setupService = setupService;
        Title = "إعداد مسؤول النظام";
    }

    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string fullName = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool isBusy;

    public string PasswordPolicyText =>
        $"اختر كلمة مرور خاصة بهذا المخبز لا تقل عن {PasswordPolicy.MinimumLength} حرفاً. لا تشاركها مع أي شخص.";

    public event EventHandler? SetupCompleted;
    public event EventHandler? PasswordResetRequested;

    partial void OnIsBusyChanged(bool value) => CreateAdministratorCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanCreateAdministrator))]
    private async Task CreateAdministratorAsync()
    {
        if (Interlocked.Exchange(ref _submissionInProgress, 1) != 0) return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _setupService.CreateInitialAdministratorAsync(
                new FirstRunAdminRequest(Username, FullName, Password, ConfirmPassword));
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "تعذر إنشاء مسؤول النظام.";
                return;
            }

            SetupCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            ErrorMessage = "تعذر إكمال الإعداد الأولي. تحقق من قاعدة البيانات ثم حاول مرة أخرى.";
        }
        finally
        {
            Password = string.Empty;
            ConfirmPassword = string.Empty;
            PasswordResetRequested?.Invoke(this, EventArgs.Empty);
            IsBusy = false;
            Interlocked.Exchange(ref _submissionInProgress, 0);
        }
    }

    private bool CanCreateAdministrator() => !IsBusy;
}
