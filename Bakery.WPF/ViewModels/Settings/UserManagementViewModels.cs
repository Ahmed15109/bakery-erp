using System.Collections.ObjectModel;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using Bakery.WPF.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class UsersViewModel : ViewModelBase
{
    private readonly IUserManagementService _userService;
    private readonly IMessageService _messageService;
    private readonly IExceptionTranslator _exceptionTranslator;
    private readonly IDialogService _dialogService;
    private readonly IUserSafePermissionService _userSafePermissionService;
    private readonly IBranchService _branchService;
    private readonly IRoleManagementService _roleService;
    private readonly IPermissionService _permissionService;
    private int _refreshVersion;

    public UsersViewModel(
        IUserManagementService userService,
        IMessageService messageService,
        IExceptionTranslator exceptionTranslator,
        IDialogService dialogService,
        IUserSafePermissionService userSafePermissionService,
        IBranchService branchService,
        IRoleManagementService roleService,
        IPermissionService permissionService)
    {
        _userService = userService;
        _messageService = messageService;
        _exceptionTranslator = exceptionTranslator;
        _dialogService = dialogService;
        _userSafePermissionService = userSafePermissionService;
        _branchService = branchService;
        _roleService = roleService;
        _permissionService = permissionService;
        Title = Loc.UsersModule;
        Users = [];
        _ = RefreshAsync();
    }

    public ObservableCollection<UserListItemDto> Users { get; }

    [ObservableProperty]
    private UserListItemDto? selectedUser;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private int totalUsers;

    [ObservableProperty]
    private int activeUsers;

    partial void OnSearchTextChanged(string value)
    {
        _ = DebouncedRefreshAsync();
    }

    private async Task DebouncedRefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        await Task.Delay(300);
        if (version == _refreshVersion)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        try
        {
            var users = await _userService.SearchAsync(SearchText);
            if (version != _refreshVersion) return;
            Users.Clear();
            foreach (var user in users)
            {
                Users.Add(user);
            }

            TotalUsers = Users.Count;
            ActiveUsers = Users.Count(user => user.IsActive);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddUser))]
    private async Task AddUserAsync()
    {
        try
        {
            var permissions = await _userService.GetPermissionsAsync();
            var branches = await _branchService.GetAllAsync();
            var safePermsRes = await _userSafePermissionService.GetUserPermissionsAsync(0);
            var roles = await _roleService.SearchAsync(null);
            var result = await _dialogService.ShowDialogAsync<UserFormDialogViewModel>(async vm =>
            {
                vm.Initialize(permissions, branches, null, safePermsRes.Permissions, roles);
                await Task.CompletedTask;
            });

            if (result.Result == true)
            {
                await _userService.CreateAsync(result.ViewModel.ToSaveRequest(null));
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditUser))]
    private async Task EditUserAsync(UserListItemDto user)
    {
        if (user is null) return;

        try
        {
            var details = await _userService.GetByIdAsync(user.Id);
            if (details is null)
            {
                _messageService.ShowError(Loc.ErrUserNotFound);
                return;
            }

            var canManageSecurity = _permissionService.HasPermission(PermissionKeys.UsersChangePermissions);
            var permissions = canManageSecurity
                ? await _userService.GetPermissionsAsync()
                : Array.Empty<PermissionDto>();
            var branches = canManageSecurity
                ? await _branchService.GetAllAsync()
                : Array.Empty<BranchDto>();
            var safePermissions = canManageSecurity
                ? (await _userSafePermissionService.GetUserPermissionsAsync(user.Id)).Permissions
                : Array.Empty<UserSafePermissionDto>();
            var roles = canManageSecurity &&
                        _permissionService.HasPermission(PermissionKeys.RolesView) &&
                        _permissionService.HasPermission(PermissionKeys.RolesAssign)
                ? await _roleService.SearchAsync(null)
                : null;
            var result = await _dialogService.ShowDialogAsync<UserFormDialogViewModel>(async vm =>
            {
                vm.Initialize(permissions, branches, details, safePermissions, roles, canManageSecurity);
                await Task.CompletedTask;
            });

            if (result.Result == true)
            {
                await _userService.UpdateAsync(result.ViewModel.ToSaveRequest(details.Id));
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditUser))]
    private async Task ToggleActiveAsync(UserListItemDto user)
    {
        if (user is null) return;

        var targetState = !user.IsActive;
        var action = targetState ? "تفعيل" : "تعطيل";
        if (!_messageService.Confirm(string.Format(Loc.ConfirmToggleActive, action, user.Username)))
        {
            return;
        }

        try
        {
            await _userService.SetActiveAsync(user.Id, targetState);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetPassword))]
    private async Task ResetPasswordAsync(UserListItemDto user)
    {
        if (user is null) return;

        var result = await _dialogService.ShowDialogAsync<ResetPasswordDialogViewModel>(async vm =>
        {
            vm.Username = user.Username;
            await Task.CompletedTask;
        });

        if (result.Result != true)
        {
            return;
        }

        try
        {
            await _userService.ResetPasswordAsync(new ResetPasswordRequest(
                user.Id,
                result.ViewModel.Password));
            _messageService.ShowInfo(Loc.MsgPasswordResetOk);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteUser))]
    private async Task DeleteUserAsync(UserListItemDto user)
    {
        if (user is null) return;

        try
        {
            if (!await _userService.CanDeleteAsync(user.Id))
            {
                _messageService.ShowError(Loc.ErrUserCannotDelete);
                return;
            }

            if (!_messageService.Confirm(string.Format(Loc.ConfirmDeleteUser, user.Username)))
            {
                return;
            }

            await _userService.DeleteAsync(user.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    private bool CanAddUser() =>
        _permissionService.HasPermission(PermissionKeys.UsersAdd) &&
        _permissionService.HasPermission(PermissionKeys.UsersChangePermissions) &&
        _permissionService.HasPermission(PermissionKeys.RolesView) &&
        _permissionService.HasPermission(PermissionKeys.RolesAssign);

    private bool CanEditUser(UserListItemDto? user) => user is not null &&
        _permissionService.HasPermission(PermissionKeys.UsersEdit);

    private bool CanResetPassword(UserListItemDto? user) => user is not null &&
        _permissionService.HasPermission(PermissionKeys.UsersResetPassword);

    private bool CanDeleteUser(UserListItemDto? user) => user is not null &&
        _permissionService.HasPermission(PermissionKeys.UsersDelete);
}

public sealed partial class UserFormDialogViewModel : ObservableObject
{
    private readonly IValidationService? _validationService;
    private readonly SemaphoreSlim _usernameValidationLock = new(1, 1);
    private bool _isHandlingCascade;
    private int _usernameValidationVersion;
    private int? _editingUserId;
    private string? _usernameValidationFailure;
    private string? _rowVersion;

    public UserFormDialogViewModel(IValidationService? validationService = null)
    {
        _validationService = validationService;
        PermissionCategories = new ObservableCollection<PermissionCategoryViewModel>();
        SafePermissions = new ObservableCollection<UserSafePermissionDto>();
        Branches = new ObservableCollection<BranchSelectionViewModel>();
        ValidationMessages = new ObservableCollection<string>();
        RefreshValidation();
    }

    public void Initialize(
        IReadOnlyList<PermissionDto> permissions,
        IReadOnlyList<BranchDto> allBranches,
        UserDetailsDto? user = null,
        IReadOnlyCollection<UserSafePermissionDto>? safePermissions = null,
        IReadOnlyCollection<RoleListItemDto>? roles = null,
        bool canManageSecurity = true)
    {
        _editingUserId = user?.Id;
        IsEditMode = user is not null;
        CanManageSecurity = canManageSecurity;
        CanManageRoles = canManageSecurity && roles is not null;
        DialogTitle = IsEditMode ? Loc.EditUser : Loc.AddUser;
        Username = user?.Username ?? string.Empty;
        FullName = user?.FullName ?? string.Empty;
        IsActive = user?.IsActive ?? true;
        _rowVersion = user?.RowVersion;

        Roles.Clear();
        var selectedRoleIds = user?.RoleIds?.ToHashSet() ?? [];
        foreach (var role in roles ?? [])
        {
            var roleVm = new RoleSelectionViewModel(role.Id, role.Name, role.Description, selectedRoleIds.Contains(role.Id));
            roleVm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(RoleSelectionViewModel.IsSelected))
                {
                    RefreshValidation();
                }
            };
            Roles.Add(roleVm);
        }

        var selectedBranches = user?.BranchIds.ToHashSet() ?? new HashSet<int>();
        Branches.Clear();
        foreach (var b in allBranches)
        {
            if (b.IsActive || selectedBranches.Contains(b.Id))
            {
                var selectionVm = new BranchSelectionViewModel(b.Id, b.Code, b.Name, selectedBranches.Contains(b.Id));
                selectionVm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(BranchSelectionViewModel.IsSelected))
                    {
                        RefreshValidation();
                    }
                };
                Branches.Add(selectionVm);
            }
        }

        var selectedKeys = user?.PermissionKeys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        PermissionCategories.Clear();
        var categories = permissions
            .GroupBy(permission => permission.Category)
            .OrderBy(group => Loc.GetPermissionCategoryName(group.Key))
            .Select(group => new PermissionCategoryViewModel(
                Loc.GetPermissionCategoryName(group.Key),
                group.OrderBy(permission => Loc.GetPermissionDisplayName(permission.Key, permission.DisplayName))
                    .Select(permission => new PermissionSelectionViewModel(
                        permission.Key,
                        Loc.GetPermissionDisplayName(permission.Key, permission.DisplayName),
                        Loc.GetPermissionCategoryName(permission.Category),
                        selectedKeys.Contains(permission.Key)))
                    .ToList()));

        foreach (var category in categories)
        {
            PermissionCategories.Add(category);
        }

        foreach (var permission in PermissionCategories.SelectMany(category => category.Permissions))
        {
            permission.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(PermissionSelectionViewModel.IsSelected))
                {
                    if (sender is PermissionSelectionViewModel vm)
                    {
                        HandlePermissionCascade(vm);
                    }
                    RefreshValidation();
                }
            };
        }

        UpdateIsEnabledStates();

        SafePermissions.Clear();
        if (safePermissions != null)
        {
            foreach (var sp in safePermissions)
            {
                SafePermissions.Add(new UserSafePermissionDto
                {
                    Id = sp.Id,
                    UserId = sp.UserId,
                    SafeId = sp.SafeId,
                    SafeName = sp.SafeName,
                    CanAccess = sp.CanAccess,
                    CanViewBalance = sp.CanViewBalance,
                    CanViewLedger = sp.CanViewLedger,
                    CanCashIn = sp.CanCashIn,
                    CanCashOut = sp.CanCashOut,
                    CanTransferFrom = sp.CanTransferFrom,
                    CanReceiveTransfer = sp.CanReceiveTransfer
                });
            }
        }
        RefreshValidation();
    }

    private void HandlePermissionCascade(PermissionSelectionViewModel permission)
    {
        if (_isHandlingCascade) return;
        _isHandlingCascade = true;

        try
        {
            var allPermissions = PermissionCategories.SelectMany(c => c.Permissions).ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

            if (permission.IsSelected)
            {
                SelectRequiredParents(permission.Key, allPermissions);
            }

            if (!permission.IsSelected)
            {
                ClearDependentPermissions(permission.Key, allPermissions);
            }

            UpdateIsEnabledStates();
        }
        finally
        {
            _isHandlingCascade = false;
        }
    }

    private static void SelectRequiredParents(
        string permissionKey,
        IReadOnlyDictionary<string, PermissionSelectionViewModel> allPermissions)
    {
        foreach (var parentKey in PermissionPolicyCatalog.GetRequiredParents(permissionKey))
        {
            if (!allPermissions.TryGetValue(parentKey, out var parent)) continue;
            parent.IsSelected = true;
            SelectRequiredParents(parentKey, allPermissions);
        }
    }

    private static void ClearDependentPermissions(
        string permissionKey,
        IReadOnlyDictionary<string, PermissionSelectionViewModel> allPermissions)
    {
        foreach (var dependentKey in PermissionPolicyCatalog.GetDependentPermissions(permissionKey))
        {
            if (!allPermissions.TryGetValue(dependentKey, out var dependent)) continue;
            dependent.IsSelected = false;
            ClearDependentPermissions(dependentKey, allPermissions);
        }
    }

    private void UpdateIsEnabledStates()
    {
        var allPermissions = PermissionCategories.SelectMany(c => c.Permissions).ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var p in allPermissions.Values)
        {
            var parentKeys = PermissionPolicyCatalog.GetRequiredParents(p.Key);
            p.IsEnabled = parentKeys.Count == 0 || parentKeys.All(parentKey =>
                allPermissions.TryGetValue(parentKey, out var parent) && parent.IsSelected);
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isEditMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool canManageSecurity = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool canManageRoles;

    [ObservableProperty]
    private string dialogTitle = string.Empty;

    public ObservableCollection<PermissionCategoryViewModel> PermissionCategories { get; }
    public ObservableCollection<UserSafePermissionDto> SafePermissions { get; }
    public ObservableCollection<BranchSelectionViewModel> Branches { get; }
    public ObservableCollection<RoleSelectionViewModel> Roles { get; } = [];
    public ObservableCollection<string> ValidationMessages { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string fullName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string confirmPassword = string.Empty;

    [ObservableProperty]
    private bool isActive = true;

    [ObservableProperty]
    private string permissionSearchText = string.Empty;

    [ObservableProperty]
    private bool? dialogResult;

    [ObservableProperty]
    private bool hasValidationErrors = true;

    [ObservableProperty]
    private bool isCheckingUsername;

    [ObservableProperty]
    private bool? isUsernameAvailable;

    public bool CanSave => !HasValidationErrors;

    partial void OnIsEditModeChanged(bool value) => RefreshValidation();
    partial void OnCanManageSecurityChanged(bool value) => RefreshValidation();
    partial void OnCanManageRolesChanged(bool value) => RefreshValidation();
    partial void OnFullNameChanged(string value) => RefreshValidation();
    partial void OnPasswordChanged(string value) => RefreshValidation();
    partial void OnConfirmPasswordChanged(string value) => RefreshValidation();

    partial void OnUsernameChanged(string value)
    {
        var version = Interlocked.Increment(ref _usernameValidationVersion);
        _usernameValidationFailure = null;
        IsUsernameAvailable = null;

        if (!IsUsernameSyntaxValid(value))
        {
            IsCheckingUsername = false;
            RefreshValidation();
            return;
        }

        if (_validationService is null)
        {
            IsCheckingUsername = false;
            IsUsernameAvailable = true;
            RefreshValidation();
            return;
        }

        IsCheckingUsername = true;
        RefreshValidation();
        _ = ValidateUsernameAvailabilityAsync(value.Trim(), _editingUserId, version);
    }

    private async Task ValidateUsernameAvailabilityAsync(string value, int? excludeId, int version)
    {
        try
        {
            await Task.Delay(300);
            if (version != _usernameValidationVersion) return;

            await _usernameValidationLock.WaitAsync();
            try
            {
                if (version != _usernameValidationVersion) return;
                var isUsed = await _validationService!.IsUsernameUsedAsync(value, excludeId);
                if (version != _usernameValidationVersion) return;
                IsUsernameAvailable = !isUsed;
            }
            finally
            {
                _usernameValidationLock.Release();
            }
        }
        catch (Exception)
        {
            if (version != _usernameValidationVersion) return;
            _usernameValidationFailure = "تعذر التحقق من توفر اسم المستخدم. أعد المحاولة.";
            IsUsernameAvailable = null;
        }
        finally
        {
            if (version == _usernameValidationVersion)
            {
                IsCheckingUsername = false;
                RefreshValidation();
            }
        }
    }

    private void RefreshValidation()
    {
        var messages = new List<string>();
        var trimmedFullName = FullName.Trim();
        var trimmedUsername = Username.Trim();

        if (trimmedFullName.Length == 0)
        {
            messages.Add("الاسم الكامل مطلوب.");
        }
        else if (trimmedFullName.Length > 150)
        {
            messages.Add("الاسم الكامل يجب ألا يتجاوز 150 حرفاً.");
        }

        if (trimmedUsername.Length == 0)
        {
            messages.Add("اسم المستخدم مطلوب.");
        }
        else if (!IsUsernameSyntaxValid(Username))
        {
            messages.Add("اسم المستخدم يجب أن يكون من 3 إلى 100 حرف وبدون مسافات.");
        }
        else if (IsCheckingUsername)
        {
            messages.Add("جارٍ التحقق من توفر اسم المستخدم...");
        }
        else if (IsUsernameAvailable == false)
        {
            messages.Add("اسم المستخدم مستخدم بالفعل. اختر اسماً آخر.");
        }
        else if (_usernameValidationFailure is not null)
        {
            messages.Add(_usernameValidationFailure);
        }

        if (!IsEditMode)
        {
            if (string.IsNullOrWhiteSpace(Password) || Password.Length < 12)
            {
                messages.Add("كلمة المرور يجب أن تتكون من 12 حرفاً على الأقل.");
            }

            if (string.IsNullOrEmpty(ConfirmPassword))
            {
                messages.Add("تأكيد كلمة المرور مطلوب.");
            }
            else if (Password != ConfirmPassword)
            {
                messages.Add("كلمة المرور وتأكيدها غير متطابقين.");
            }
        }

        if (CanManageSecurity)
        {
            if (!Branches.Any(branch => branch.IsSelected))
            {
                messages.Add("يجب اختيار فرع واحد على الأقل.");
            }

            if (CanManageRoles)
            {
                if (!Roles.Any(role => role.IsSelected))
                {
                    messages.Add("يجب اختيار دور وظيفي واحد على الأقل.");
                }
            }
            else if (!IsEditMode)
            {
                messages.Add("لا تتوفر أدوار وظيفية للتعيين. تحقق من صلاحيات إدارة الأدوار.");
            }
        }

        ValidationMessages.Clear();
        foreach (var message in messages)
        {
            ValidationMessages.Add(message);
        }

        HasValidationErrors = messages.Count > 0;
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private static bool IsUsernameSyntaxValid(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length is >= 3 and <= 100 && !value.Any(char.IsWhiteSpace);
    }

    partial void OnPermissionSearchTextChanged(string value)
    {
        var search = value.Trim();
        foreach (var category in PermissionCategories)
        {
            category.ApplySearch(search);
        }
    }

    public SaveUserRequest ToSaveRequest(int? userId)
    {
        return new SaveUserRequest(
            userId,
            Username.Trim(),
            FullName.Trim(),
            string.IsNullOrWhiteSpace(Password) ? null : Password,
            IsActive,
            CanManageSecurity
                ? PermissionCategories
                    .SelectMany(category => category.Permissions)
                    .Where(permission => permission.IsSelected)
                    .Select(permission => permission.Key)
                    .ToArray()
                : null,
            CanManageSecurity
                ? Branches.Where(b => b.IsSelected).Select(b => b.Id).ToArray()
                : null,
            CanManageRoles ? Roles.Where(role => role.IsSelected).Select(role => role.Id).ToArray() : null,
            CanManageSecurity ? SafePermissions.ToArray() : null,
            _rowVersion);
    }

    [RelayCommand]
    private void SelectAll()
    {
        SetAllPermissions(true);
    }

    [RelayCommand]
    private void UnselectAll()
    {
        SetAllPermissions(false);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        DialogResult = true;
    }

    private void SetAllPermissions(bool isSelected)
    {
        foreach (var permission in PermissionCategories.SelectMany(category => category.Permissions))
        {
            permission.IsSelected = isSelected;
        }

        SaveCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class PermissionCategoryViewModel : ObservableObject
{
    public PermissionCategoryViewModel(string name, IReadOnlyList<PermissionSelectionViewModel> permissions)
    {
        Name = name;
        Permissions = new ObservableCollection<PermissionSelectionViewModel>(permissions);

        foreach (var permission in Permissions)
        {
            permission.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PermissionSelectionViewModel.IsVisible))
                {
                    IsVisible = Permissions.Any(item => item.IsVisible);
                }
            };
        }
    }

    public string Name { get; }
    public ObservableCollection<PermissionSelectionViewModel> Permissions { get; }

    [ObservableProperty]
    private bool isVisible = true;

    [RelayCommand]
    private void SelectAllCategory()
    {
        foreach (var p in Permissions)
        {
            if (p.IsEnabled) p.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearAllCategory()
    {
        foreach (var p in Permissions)
        {
            if (p.IsEnabled) p.IsSelected = false;
        }
    }

    public void ApplySearch(string searchText)
    {
        foreach (var permission in Permissions)
        {
            permission.IsVisible =
                string.IsNullOrWhiteSpace(searchText) ||
                permission.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                permission.Key.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                permission.Category.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        IsVisible = Permissions.Any(permission => permission.IsVisible);
    }
}

public sealed partial class PermissionSelectionViewModel : ObservableObject
{
    public PermissionSelectionViewModel(string key, string displayName, string category, bool isSelected)
    {
        Key = key;
        DisplayName = displayName;
        Category = category;
        IsSelected = isSelected;
        IsEnabled = true;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string Description => Loc.GetPermissionDescription(Key);

    public bool IsDangerous => Key.EndsWith(".Delete", StringComparison.OrdinalIgnoreCase) ||
                               Key == PermissionKeys.WorkingDayReopen ||
                               Key == PermissionKeys.SettingsSystem ||
                               Key == PermissionKeys.SettingsResetSystem ||
                               Key == PermissionKeys.TreasuryManageSafes ||
                               Key == PermissionKeys.EmployeesAdvances ||
                               Key == PermissionKeys.UsersChangePermissions;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isVisible = true;

    [ObservableProperty]
    private bool isEnabled = true;
}

public sealed partial class ResetPasswordDialogViewModel : ObservableObject
{
    public ResetPasswordDialogViewModel()
    {
    }

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string confirmPassword = string.Empty;

    [ObservableProperty]
    private bool? dialogResult;

    public bool CanSave => Password.Length >= 12 && Password == ConfirmPassword;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        DialogResult = true;
    }
}
 
public sealed partial class BranchSelectionViewModel : ObservableObject
{
    public BranchSelectionViewModel(int id, string code, string name, bool isSelected)
    {
        Id = id;
        Code = code;
        Name = name;
        IsSelected = isSelected;
    }
 
    public int Id { get; }
    public string Code { get; }
    public string Name { get; }
 
    [ObservableProperty]
    private bool isSelected;
}

public sealed partial class RoleSelectionViewModel : ObservableObject
{
    public RoleSelectionViewModel(int id, string name, string? description, bool isSelected)
    {
        Id = id;
        Name = name;
        Description = description;
        IsSelected = isSelected;
    }

    public int Id { get; }
    public string Name { get; }
    public string? Description { get; }

    [ObservableProperty]
    private bool isSelected;
}
