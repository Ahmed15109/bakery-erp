using System.Collections.ObjectModel;
using System.Threading;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Shared.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private int _loginInProgress;
    private int _branchRefreshVersion;
    private bool _suppressBranchRefresh;

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
        Title = Loc.Login;
        InitializationTask = LoadAsync();
    }

    public Task InitializationTask { get; }
    public Task PendingUserRefresh { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private BranchDto? selectedBranch;

    public ObservableCollection<BranchDto> Branches { get; } = [];
    public ObservableCollection<UserDto> Users { get; } = [];

    [ObservableProperty]
    private UserDto? selectedUser;

    public event EventHandler? LoginSucceeded;
    public event EventHandler? LoginFailed;

    partial void OnSelectedBranchChanged(BranchDto? value)
    {
        if (_suppressBranchRefresh)
        {
            return;
        }

        Password = string.Empty;
        SelectedUser = null;
        Users.Clear();
        var version = Interlocked.Increment(ref _branchRefreshVersion);
        PendingUserRefresh = RefreshUsersAsync(value, version);
    }

    private async Task LoadAsync()
    {
        await _operationGate.WaitAsync();
        SetBusy(true);
        try
        {
            ErrorMessage = null;
            var activeBranches = await _authService.GetActiveBranchesAsync();
            Branches.Clear();
            foreach (var branch in activeBranches)
            {
                Branches.Add(branch);
            }

            _suppressBranchRefresh = true;
            SelectedBranch = Branches.FirstOrDefault();
            _suppressBranchRefresh = false;

            if (SelectedBranch is not null)
            {
                var version = Interlocked.Increment(ref _branchRefreshVersion);
                await LoadUsersCoreAsync(SelectedBranch, version);
            }
            else
            {
                ErrorMessage = "لا توجد فروع نشطة متاحة لتسجيل الدخول.";
            }
        }
        catch
        {
            ErrorMessage = "تعذر تحميل الفروع. يرجى المحاولة مرة أخرى.";
        }
        finally
        {
            _suppressBranchRefresh = false;
            SetBusy(false);
            _operationGate.Release();
        }
    }

    private async Task RefreshUsersAsync(BranchDto? branch, int version)
    {
        await _operationGate.WaitAsync();
        SetBusy(true);
        try
        {
            if (branch is null || version != _branchRefreshVersion || SelectedBranch?.Id != branch.Id)
            {
                return;
            }

            ErrorMessage = null;
            await LoadUsersCoreAsync(branch, version);
        }
        catch
        {
            if (version == _branchRefreshVersion)
            {
                Users.Clear();
                SelectedUser = null;
                ErrorMessage = "تعذر تحميل مستخدمي الفرع المحدد. يرجى المحاولة مرة أخرى.";
            }
        }
        finally
        {
            SetBusy(false);
            _operationGate.Release();
        }
    }

    private async Task LoadUsersCoreAsync(BranchDto branch, int version)
    {
        var users = await _authService.GetUsersForBranchAsync(branch.Id);
        if (version != _branchRefreshVersion || SelectedBranch?.Id != branch.Id)
        {
            return;
        }

        Users.Clear();
        foreach (var user in users)
        {
            Users.Add(user);
        }

        SelectedUser = Users.FirstOrDefault();
        if (Users.Count == 0)
        {
            ErrorMessage = "لا يوجد مستخدمون نشطون مصرح لهم بالدخول إلى هذا الفرع.";
        }
    }

    private bool CanSubmitLogin() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSubmitLogin))]
    private async Task LoginAsync()
    {
        if (Interlocked.Exchange(ref _loginInProgress, 1) != 0)
        {
            return;
        }

        var resetFormAfterFailure = true;
        await _operationGate.WaitAsync();
        SetBusy(true);
        try
        {
            ErrorMessage = null;
            if (SelectedBranch == null)
            {
                ErrorMessage = Loc.ErrNoBranchSelected;
                return;
            }

            if (SelectedUser is null)
            {
                ErrorMessage = "اختر اسم المستخدم أولاً.";
                return;
            }

            var selectedLoginUser = Users.FirstOrDefault(user =>
                user.Id == SelectedUser.Id &&
                string.Equals(user.Username, SelectedUser.Username, StringComparison.OrdinalIgnoreCase));
            if (selectedLoginUser is null)
            {
                ErrorMessage = "المستخدم غير نشط أو غير مصرح له بالدخول إلى الفرع المحدد.";
                return;
            }

            if (string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "أدخل كلمة المرور.";
                return;
            }

            var result = await _authService.LoginAsync(new LoginRequest(selectedLoginUser.Username, Password, SelectedBranch.Id));
            if (!result.Succeeded || result.User is null)
            {
                ErrorMessage = result.ErrorMessage ?? Loc.ErrInvalidCredentials;
                return;
            }

            LoginSucceeded?.Invoke(this, EventArgs.Empty);
            resetFormAfterFailure = false;
        }
        catch
        {
            await SafeLogoutAsync();
            ErrorMessage = "تعذر تسجيل الدخول. تحقق من البيانات وحاول مرة أخرى.";
        }
        finally
        {
            SetBusy(false);
            _operationGate.Release();
            Interlocked.Exchange(ref _loginInProgress, 0);
            if (resetFormAfterFailure)
            {
                ResetFailedLoginState();
            }
        }
    }

    private void ResetFailedLoginState()
    {
        RunOnUiThread(() =>
        {
            Password = string.Empty;
            if (string.IsNullOrWhiteSpace(ErrorMessage))
            {
                ErrorMessage = Loc.ErrInvalidCredentials;
            }

            LoginFailed?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task SafeLogoutAsync()
    {
        try
        {
            await _authService.LogoutAsync();
        }
        catch
        {
            // The login form must always be restored even if logout auditing fails.
        }
    }

    private void SetBusy(bool value)
    {
        RunOnUiThread(() =>
        {
            IsBusy = value;
            LoginCommand.NotifyCanExecuteChanged();
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null &&
            !dispatcher.HasShutdownStarted &&
            !dispatcher.HasShutdownFinished &&
            !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
            return;
        }

        action();
    }
}

public sealed partial class ChangePasswordDialogViewModel : ObservableObject
{
    public string CurrentPassword { get; private set; } = string.Empty;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string newPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string confirmPassword = string.Empty;

    [ObservableProperty]
    private bool? dialogResult;

    public bool CanSave => NewPassword.Length >= 12 && NewPassword == ConfirmPassword;

    public void Initialize(string username, string currentPassword)
    {
        Username = username;
        CurrentPassword = currentPassword;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => DialogResult = true;
}
