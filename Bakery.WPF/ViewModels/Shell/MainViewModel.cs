using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Infrastructure.Data;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

using Bakery.Application.DTOs.Accounting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakery.WPF.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly IAuthService _authService;
    private readonly IUserSessionService _userSessionService;
    private readonly IPermissionService _permissionService;
    private readonly IBranchContext _branchContext;
    private readonly ISafeContext _safeContext;
    private readonly ISafeSwitchService _safeSwitchService;
    private readonly ISafeService _safeService;
    private readonly IDialogService _dialogService;
    private readonly IBranchService _branchService;
    private readonly IBranchSwitchService _branchSwitchService;
    private readonly BakeryDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessageService _messageService;
    private readonly IWorkingDayService? _workingDayService;
    private readonly IOperationalContextRefreshNotifier? _refreshNotifier;

    public MainViewModel(
        INavigationService navigationService,
        IAuthService authService,
        IUserSessionService userSessionService,
        IPermissionService permissionService,
        IBranchContext branchContext,
        ISafeContext safeContext,
        ISafeSwitchService safeSwitchService,
        ISafeService safeService,
        IDialogService dialogService,
        IBranchService branchService,
        IBranchSwitchService branchSwitchService,
        BakeryDbContext dbContext,
        IServiceProvider serviceProvider,
        IMessageService messageService,
        IWorkingDayService? workingDayService = null,
        IOperationalContextRefreshNotifier? refreshNotifier = null)
    {
        _navigationService = navigationService;
        _authService = authService;
        _userSessionService = userSessionService;
        _permissionService = permissionService;
        _branchContext = branchContext;
        _safeContext = safeContext;
        _safeSwitchService = safeSwitchService;
        _safeService = safeService;
        _dialogService = dialogService;
        _branchService = branchService;
        _branchSwitchService = branchSwitchService;
        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
        _messageService = messageService;
        _workingDayService = workingDayService;
        _refreshNotifier = refreshNotifier;
        Title = Loc.AppTitle;
        CurrentUserName = _userSessionService.CurrentUser?.DisplayName ?? string.Empty;
        CurrentBranchName = _branchContext.CurrentBranch?.Name ?? string.Empty;
        CurrentSafeName = _safeContext.CurrentSafe?.DisplayName ?? "لا توجد خزنة";
        NavigationItems = CreateNavigationItems();
        _userSessionService.AuthorizationChanged += OnAuthorizationChanged;
        if (_refreshNotifier is not null) _refreshNotifier.RefreshRequested += OnOperationalRefreshRequested;

        _safeContext.SafeChanged += (_, args) =>
        {
            CurrentSafeName = args.NewSafe?.DisplayName ?? "لا توجد خزنة";
        };

        if (_navigationService is INotifyPropertyChanged notifyingNavigationService)
        {
            notifyingNavigationService.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(INavigationService.CurrentViewModel))
                {
                    OnPropertyChanged(nameof(CurrentViewModel));
                    UpdateNavigationActiveState();
                }
            };
        }

        InitializationTask = InitializeAsync();
    }

    public Task InitializationTask { get; }

    private async Task InitializeAsync()
    {
        // All services in a desktop session share one scoped DbContext. Keep
        // startup queries sequential and await the initial dashboard refresh.
        await LoadBranchCountAsync();
        await LoadSafeCountAsync();
        await LoadWorkingDayHeaderAsync();
        NavigationItems.FirstOrDefault()?.Navigate();
        if (_navigationService.CurrentViewModel is DashboardViewModel dashboard)
        {
            await dashboard.InitializationTask;
        }
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public ObservableObject? CurrentViewModel => _navigationService.CurrentViewModel;

    [ObservableProperty]
    private string currentUserName = string.Empty;

    [ObservableProperty]
    private string currentBranchName = string.Empty;

    [ObservableProperty]
    private bool canSwitchBranch;

    [ObservableProperty]
    private string currentWorkingDayStatus = "يوم العمل: —";

    public event EventHandler? LoggedOut;

    private ObservableCollection<NavigationItemViewModel> CreateNavigationItems()
    {
        // 1. Sub-items definition
        var rawEmployeeSubItems = new[]
        {
            new NavigationItemViewModel("بيانات الموظفين", "AccountGroup", typeof(EmployeesViewModel), () => _navigationService.NavigateTo<EmployeesViewModel>()),
            new NavigationItemViewModel(Loc.EmployeeSettlements, "AccountCash", typeof(SettlementViewModel), () => _navigationService.NavigateTo<SettlementViewModel>()),
            new NavigationItemViewModel(Loc.EmployeeJobs, "BriefcaseAccount", typeof(JobRolesViewModel), () => _navigationService.NavigateTo<JobRolesViewModel>())
        };

        var allowedEmployeeSubItems = rawEmployeeSubItems
            .Where(sub => sub.PermissionKeys.Count == 0 || sub.PermissionKeys.Any(_permissionService.HasPermission))
            .ToArray();

        var rawUserSubItems = new[]
        {
            new NavigationItemViewModel(Loc.UsersModule, "AccountCog", typeof(UsersViewModel), () => _navigationService.NavigateTo<UsersViewModel>()),
            new NavigationItemViewModel("الأدوار والصلاحيات", "ShieldAccount", typeof(RolesViewModel), () => _navigationService.NavigateTo<RolesViewModel>())
        };

        var allowedUserSubItems = rawUserSubItems
            .Where(sub => sub.PermissionKeys.Count == 0 || sub.PermissionKeys.Any(_permissionService.HasPermission))
            .ToArray();

        var itemsList = new List<NavigationItemViewModel>();

        // ── Group 1: Operational (التشغيل) ──────────────────────────
        itemsList.Add(new NavigationItemViewModel(Loc.Dashboard, "ViewDashboard", typeof(DashboardViewModel), () => _navigationService.NavigateTo<DashboardViewModel>(), groupId: 1));
        itemsList.Add(new NavigationItemViewModel(Loc.Invoices, "Receipt", typeof(InvoiceWorkspaceViewModel), () => _navigationService.NavigateTo<InvoiceWorkspaceViewModel>(), groupId: 1));
        itemsList.Add(new NavigationItemViewModel(Loc.Inventory, "PackageVariantClosed", typeof(InventoryHomeViewModel), () => _navigationService.NavigateTo<InventoryHomeViewModel>(), groupId: 1));
        itemsList.Add(new NavigationItemViewModel(Loc.Production, "Factory", typeof(ProductionViewModel), () => _navigationService.NavigateTo<ProductionViewModel>(), groupId: 1));
        itemsList.Add(new NavigationItemViewModel(Loc.Waste, "DeleteSweep", typeof(WasteViewModel), () => _navigationService.NavigateTo<WasteViewModel>(), groupId: 1));

        // ── Group 2: Business Management (إدارة الأعمال) ───────────
        if (allowedEmployeeSubItems.Length > 0)
        {
            itemsList.Add(new NavigationItemViewModel(Loc.Employees, "AccountGroup", allowedEmployeeSubItems, groupId: 2));
        }
        itemsList.Add(new NavigationItemViewModel(Loc.Accounts, "BookOpenVariant", typeof(PartiesViewModel), () => _navigationService.NavigateTo<PartiesViewModel>(), groupId: 2));
        itemsList.Add(new NavigationItemViewModel(Loc.Safes, "Safe", typeof(TreasuryViewModel), () => _navigationService.NavigateTo<TreasuryViewModel>(), groupId: 2));
        itemsList.Add(new NavigationItemViewModel(Loc.Reports, "ChartBox", typeof(ReportsViewModel), () => _navigationService.NavigateTo<ReportsViewModel>(), groupId: 2));

        // ── Group 3: Administration (الإدارة والفروع) ───────────────
        itemsList.Add(new NavigationItemViewModel(Loc.BranchesModule, "Domain", typeof(BranchesViewModel), () => _navigationService.NavigateTo<BranchesViewModel>(), groupId: 3));
        if (allowedUserSubItems.Length > 0)
        {
            itemsList.Add(new NavigationItemViewModel("المستخدمون", "AccountKey", allowedUserSubItems, groupId: 3));
        }

        // ── Group 4: System (النظام والمراقبة) ──────────────────────
        itemsList.Add(new NavigationItemViewModel("سجل التدقيق", "ClipboardTextClock", typeof(AuditLogViewModel), () => _navigationService.NavigateTo<AuditLogViewModel>(), groupId: 4));
        itemsList.Add(new NavigationItemViewModel("النسخ الاحتياطي", "DatabaseSyncOutline", typeof(BackupManagementViewModel), () => _navigationService.NavigateTo<BackupManagementViewModel>(), groupId: 4));
        itemsList.Add(new NavigationItemViewModel(Loc.Settings, "Cog", typeof(SettingsViewModel), () => _navigationService.NavigateTo<SettingsViewModel>(), groupId: 4));

        // Filter items by permission
        var allowedItems = itemsList
            .Where(item => item.HasSubItems || item.PermissionKeys.Count == 0 || item.PermissionKeys.Any(_permissionService.HasPermission))
            .ToArray();

        // Assign top spacing dynamically between distinct logical groups
        int? previousGroupId = null;
        foreach (var item in allowedItems)
        {
            item.ConfigureAuthorization(() =>
                item.HasSubItems || item.PermissionKeys.Count == 0 || item.PermissionKeys.Any(_permissionService.HasPermission));

            if (previousGroupId.HasValue && item.GroupId != previousGroupId.Value)
            {
                item.GroupMargin = new Thickness(0, 18, 0, 0);
            }
            else
            {
                item.GroupMargin = new Thickness(0, 0, 0, 0);
            }
            previousGroupId = item.GroupId;
        }

        return new ObservableCollection<NavigationItemViewModel>(allowedItems);
    }

    private void UpdateNavigationActiveState()
    {
        var currentVmType = CurrentViewModel?.GetType();
        foreach (var item in NavigationItems)
        {
            if (item.HasSubItems)
            {
                var isAnySubItemSelected = false;
                foreach (var subItem in item.SubItems)
                {
                    subItem.IsSelected = subItem.TargetType == currentVmType;
                    if (subItem.IsSelected)
                    {
                        isAnySubItemSelected = true;
                    }
                }

                item.IsSubItemSelected = isAnySubItemSelected;
                item.IsSelected = isAnySubItemSelected;

                if (isAnySubItemSelected)
                {
                    item.IsExpanded = true;
                }
            }
            else
            {
                item.IsSelected = item.TargetType == currentVmType;
            }
        }
    }

    private async Task LoadBranchCountAsync()
    {
        var userId = _userSessionService.CurrentUser?.UserId;
        if (userId == null)
        {
            CanSwitchBranch = false;
            return;
        }

        try
        {
            var branches = await _branchService.GetUserBranchesAsync(userId.Value);
            var branchCount = branches?.Count ?? 0;
            var hasPermission = _permissionService.HasPermission(PermissionKeys.BranchesSwitch);
            CanSwitchBranch = hasPermission && branchCount > 1;
        }
        catch
        {
            CanSwitchBranch = false;
        }
    }

    [RelayCommand]
    private async Task SwitchBranchAsync()
    {
        var userId = _userSessionService.CurrentUser?.UserId;
        if (userId == null) return;

        // Security Check: enforce the Switch Branch permission
        if (!_permissionService.HasPermission(PermissionKeys.BranchesSwitch))
        {
            return;
        }

        try
        {
            var branches = await _branchService.GetUserBranchesAsync(userId.Value);
            // UX optimization: if only 0 or 1 branch is available, prevent switching
            if (branches == null || branches.Count <= 1)
            {
                return;
            }

            var result = await _dialogService.ShowDialogAsync<BranchSelectionDialogViewModel>(async vm =>
            {
                vm.Initialize(branches, _branchContext.CurrentBranch);
                await Task.CompletedTask;
            });

            if (result.Result == true && result.ViewModel.SelectedBranch != null)
            {
                var selected = result.ViewModel.SelectedBranch;
                if (_branchContext.CurrentBranch?.Id != selected.Id)
                {
                    // Save active VM type to reload it after branch switch
                    var activeVmType = _navigationService.CurrentViewModel?.GetType();

                    // Navigate to Dashboard first to dispose and unsubscribe the active VM
                    if (activeVmType != typeof(DashboardViewModel))
                    {
                        _navigationService.NavigateTo<DashboardViewModel>();
                    }

                    _dbContext.ChangeTracker.Clear();
                    await _branchSwitchService.SwitchBranchAsync(selected);
                    CurrentBranchName = selected.Name;

                    // Await safe count sequentially after branch selection is complete
                    await LoadSafeCountAsync();

                    // Navigate back to original VM type (or Dashboard if none)
                    if (activeVmType != null && activeVmType != typeof(DashboardViewModel))
                    {
                        var method = typeof(INavigationService)
                            .GetMethod(nameof(INavigationService.NavigateTo))
                            ?.MakeGenericMethod(activeVmType);
                        method?.Invoke(_navigationService, null);
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _messageService.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Open working-day close"));
        }
    }

    [ObservableProperty]
    private string currentSafeName = string.Empty;

    [ObservableProperty]
    private bool canSwitchSafe;

    private async Task LoadSafeCountAsync()
    {
        var userId = _userSessionService.CurrentUser?.UserId;
        if (userId == null)
        {
            CanSwitchSafe = false;
            return;
        }

        try
        {
            var safes = await _safeService.ListSafesAsync();
            var safeCount = safes?.Count ?? 0;
            CanSwitchSafe = safeCount > 1;
        }
        catch
        {
            CanSwitchSafe = false;
        }
    }

    private async Task LoadWorkingDayHeaderAsync()
    {
        if (_workingDayService is null || !_permissionService.HasAnyPermission(
                PermissionKeys.WorkingDayView,
                PermissionKeys.WorkingDayReopen,
                PermissionKeys.TreasuryView))
        {
            CurrentWorkingDayStatus = "يوم العمل: —";
            return;
        }

        try
        {
            var summary = await _workingDayService.GetCurrentDaySummaryAsync();
            CurrentWorkingDayStatus = summary is null
                ? "يوم العمل: غير مسجل"
                : $"يوم العمل: {(summary.Status == Bakery.Domain.Enums.WorkingDayStatus.Open ? "مفتوح" : "مغلق")} • {summary.BusinessDate:dd/MM/yyyy}";
        }
        catch
        {
            CurrentWorkingDayStatus = "يوم العمل: —";
        }
    }

    private async Task OnOperationalRefreshRequested()
    {
        await LoadWorkingDayHeaderAsync();
    }

    [RelayCommand]
    private async Task SwitchSafeAsync()
    {
        var userId = _userSessionService.CurrentUser?.UserId;
        if (userId == null) return;

        try
        {
            var safes = await _safeService.ListSafesAsync();
            if (safes == null || safes.Count <= 1)
            {
                return;
            }

            var result = await _dialogService.ShowDialogAsync<SafeSelectionDialogViewModel>(async vm =>
            {
                vm.Initialize(safes, _safeContext.CurrentSafe);
                await Task.CompletedTask;
            });

            if (result.Result == true && result.ViewModel.SelectedSafe != null)
            {
                var selected = result.ViewModel.SelectedSafe;
                if (_safeContext.CurrentSafe?.Id != selected.Id)
                {
                    _dbContext.ChangeTracker.Clear();
                    await _safeSwitchService.SwitchSafeAsync(selected);
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _messageService.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Open next working day"));
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _authService.LogoutAsync();
        // Note: _branchContext.Clear() is already called inside AuthService.LogoutAsync
        CurrentBranchName = string.Empty;
        CurrentSafeName = "لا توجد خزنة";
    }

    private void OnAuthorizationChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (!_userSessionService.IsAuthenticated)
            {
                LoggedOut?.Invoke(this, EventArgs.Empty);
                return;
            }

            var refreshed = CreateNavigationItems();
            NavigationItems.Clear();
            foreach (var item in refreshed)
            {
                NavigationItems.Add(item);
            }

            var currentType = _navigationService.CurrentViewModel?.GetType();
            var required = currentType is null
                ? Array.Empty<string>()
                : Bakery.WPF.Authorization.NavigationAuthorizationPolicy.GetRequiredPermissions(currentType);
            if (required.Count > 0 && !required.Any(_permissionService.HasPermission))
            {
                _navigationService.NavigateTo<DashboardViewModel>();
            }
        });
    }

    public void Dispose()
    {
        _userSessionService.AuthorizationChanged -= OnAuthorizationChanged;
        if (_refreshNotifier is not null) _refreshNotifier.RefreshRequested -= OnOperationalRefreshRequested;
    }
}
