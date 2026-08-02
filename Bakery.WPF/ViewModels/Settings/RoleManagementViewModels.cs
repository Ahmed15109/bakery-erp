using System.Collections.ObjectModel;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class RolesViewModel : ViewModelBase
{
    private readonly IRoleManagementService _roleService;
    private readonly IUserManagementService _userService;
    private readonly IDialogService _dialogService;
    private readonly IMessageService _messageService;
    private readonly IExceptionTranslator _exceptionTranslator;
    private readonly IPermissionService _permissionService;
    private int _refreshVersion;

    public RolesViewModel(
        IRoleManagementService roleService,
        IUserManagementService userService,
        IDialogService dialogService,
        IMessageService messageService,
        IExceptionTranslator exceptionTranslator,
        IPermissionService permissionService)
    {
        _roleService = roleService;
        _userService = userService;
        _dialogService = dialogService;
        _messageService = messageService;
        _exceptionTranslator = exceptionTranslator;
        _permissionService = permissionService;
        Title = "الأدوار الوظيفية والصلاحيات";
        _ = RefreshAsync();
    }

    public ObservableCollection<RoleListItemDto> Roles { get; } = [];

    [ObservableProperty]
    private string searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => _ = DebouncedRefreshAsync();

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
            IsBusy = true;
            var roles = await _roleService.SearchAsync(SearchText);
            if (version != _refreshVersion) return;
            Roles.Clear();
            foreach (var role in roles) Roles.Add(role);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        try
        {
            var permissions = await _userService.GetPermissionsAsync();
            var result = await _dialogService.ShowDialogAsync<RoleFormDialogViewModel>(vm =>
            {
                vm.Initialize(permissions);
                return Task.CompletedTask;
            });
            if (result.Result == true)
            {
                await _roleService.CreateAsync(result.ViewModel.ToRequest());
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAsync(RoleListItemDto? role)
    {
        if (role is null) return;
        try
        {
            var details = await _roleService.GetByIdAsync(role.Id);
            if (details is null) return;
            var permissions = await _userService.GetPermissionsAsync();
            var result = await _dialogService.ShowDialogAsync<RoleFormDialogViewModel>(vm =>
            {
                vm.Initialize(permissions, details);
                return Task.CompletedTask;
            });
            if (result.Result == true)
            {
                await _roleService.UpdateAsync(result.ViewModel.ToRequest());
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(RoleListItemDto? role)
    {
        if (role is null || !_messageService.Confirm($"هل تريد حذف الدور «{role.Name}»؟")) return;
        try
        {
            await _roleService.DeleteAsync(role.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    private bool CanAdd() => _permissionService.HasPermission(PermissionKeys.RolesAdd) &&
        _permissionService.HasPermission(PermissionKeys.UsersChangePermissions);

    private bool CanEdit(RoleListItemDto? role) => role is not null &&
        _permissionService.HasPermission(PermissionKeys.RolesEdit) &&
        _permissionService.HasPermission(PermissionKeys.UsersChangePermissions);

    private bool CanDelete(RoleListItemDto? role) => role is { IsProtected: false } &&
        _permissionService.HasPermission(PermissionKeys.RolesDelete);
}

public sealed partial class RoleFormDialogViewModel : ObservableObject
{
    private int? _roleId;
    private string? _rowVersion;
    private bool _updatingDependencies;

    public ObservableCollection<RolePermissionCategoryViewModel> Categories { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string name = string.Empty;

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private string dialogTitle = "إضافة دور";

    [ObservableProperty]
    private bool? dialogResult;

    public bool CanSave => Name.Trim().Length is >= 3 and <= 120 &&
        Categories.SelectMany(category => category.Permissions).Any(permission => permission.IsSelected);

    public void Initialize(IReadOnlyCollection<PermissionDto> permissions, RoleDetailsDto? role = null)
    {
        _roleId = role?.Id;
        _rowVersion = role?.RowVersion;
        Name = role?.Name ?? string.Empty;
        Description = role?.Description;
        DialogTitle = role is null ? "إضافة دور" : "تعديل الدور";
        var selected = role?.PermissionKeys.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        Categories.Clear();
        foreach (var group in permissions.GroupBy(permission => permission.Category)
                     .OrderBy(group => Loc.GetPermissionCategoryName(group.Key)))
        {
            var category = new RolePermissionCategoryViewModel(
                Loc.GetPermissionCategoryName(group.Key),
                group.OrderBy(permission => Loc.GetPermissionDisplayName(permission.Key, permission.DisplayName))
                    .Select(permission => new RolePermissionSelectionViewModel(
                        permission.Key,
                        Loc.GetPermissionDisplayName(permission.Key, permission.DisplayName),
                        Loc.GetPermissionDescription(permission.Key),
                        selected.Contains(permission.Key)))
                    .ToArray());
            foreach (var permission in category.Permissions)
            {
                permission.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(RolePermissionSelectionViewModel.IsSelected))
                    {
                        ApplyDependencies(permission);
                        SaveCommand.NotifyCanExecuteChanged();
                        OnPropertyChanged(nameof(CanSave));
                    }
                };
            }
            Categories.Add(category);
        }
    }

    private void ApplyDependencies(RolePermissionSelectionViewModel changed)
    {
        if (_updatingDependencies) return;
        _updatingDependencies = true;
        try
        {
            var all = Categories.SelectMany(category => category.Permissions)
                .ToDictionary(permission => permission.Key, StringComparer.OrdinalIgnoreCase);
            if (changed.IsSelected)
            {
                SelectParents(changed.Key, all);
            }
            else
            {
                ClearDependents(changed.Key, all);
            }
        }
        finally
        {
            _updatingDependencies = false;
        }
    }

    private static void SelectParents(string key, IReadOnlyDictionary<string, RolePermissionSelectionViewModel> all)
    {
        foreach (var parentKey in PermissionPolicyCatalog.GetRequiredParents(key))
        {
            if (!all.TryGetValue(parentKey, out var parent)) continue;
            parent.IsSelected = true;
            SelectParents(parentKey, all);
        }
    }

    private static void ClearDependents(string key, IReadOnlyDictionary<string, RolePermissionSelectionViewModel> all)
    {
        foreach (var dependentKey in PermissionPolicyCatalog.GetDependentPermissions(key))
        {
            if (!all.TryGetValue(dependentKey, out var dependent)) continue;
            dependent.IsSelected = false;
            ClearDependents(dependentKey, all);
        }
    }

    public SaveRoleRequest ToRequest() => new(
        _roleId,
        Name.Trim(),
        string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        Categories.SelectMany(category => category.Permissions)
            .Where(permission => permission.IsSelected)
            .Select(permission => permission.Key)
            .ToArray(),
        _rowVersion);

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var permission in Categories.SelectMany(category => category.Permissions)) permission.IsSelected = true;
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var permission in Categories.SelectMany(category => category.Permissions)) permission.IsSelected = false;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => DialogResult = true;
}

public sealed class RolePermissionCategoryViewModel
{
    public RolePermissionCategoryViewModel(string name, IReadOnlyCollection<RolePermissionSelectionViewModel> permissions)
    {
        Name = name;
        Permissions = permissions;
    }

    public string Name { get; }
    public IReadOnlyCollection<RolePermissionSelectionViewModel> Permissions { get; }
}

public sealed partial class RolePermissionSelectionViewModel : ObservableObject
{
    public RolePermissionSelectionViewModel(string key, string name, string description, bool selected)
    {
        Key = key;
        Name = name;
        Description = description;
        IsSelected = selected;
    }

    public string Key { get; }
    public string Name { get; }
    public string Description { get; }

    [ObservableProperty]
    private bool isSelected;
}
