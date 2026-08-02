using System.Collections.ObjectModel;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.DTOs.Waste;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using Bakery.WPF.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakery.Application.Security;

namespace Bakery.WPF.ViewModels;


public sealed partial class WasteViewModel : ViewModelBase
{
    private readonly IWasteService _service;
    private readonly IItemService _itemService;
    private readonly IStockCalculationService _stock;
    private readonly IMessageService _messages;
    private readonly IPermissionService _permissionService;

    public WasteViewModel(IWasteService service, IItemService itemService, IStockCalculationService stock, IMessageService messages, IPermissionService permissionService)
    {
        _service = service;
        _itemService = itemService;
        _stock = stock;
        _messages = messages;
        _permissionService = permissionService;
        Title = "الهالك والتالف";
        Entries = [];
        FilterItems = [];
        _ = RefreshAsync();
    }

    public ObservableCollection<WasteEntryDto> Entries { get; }
    public ObservableCollection<ItemDto> FilterItems { get; }

    [ObservableProperty] private int todayCount;
    [ObservableProperty] private decimal todayQuantity;
    [ObservableProperty] private decimal todayCost;

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private string? selectedReason;
    [ObservableProperty] private ItemDto? selectedFilterItem;

    public IReadOnlyList<string> Reasons { get; } =
    [
        "تلف", "انتهاء صلاحية", "كسر", "حرق", "عيب تصنيع", "مرتجع غير صالح", "سبب آخر"
    ];

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadSummaryAsync();
        await LoadEntriesAsync();
        await LoadFilterItemsAsync();
    }

    private async Task LoadSummaryAsync()
    {
        var summary = await _service.GetTodaySummaryAsync();
        TodayCount = summary.TodayCount;
        TodayQuantity = summary.TodayQuantity;
        TodayCost = summary.TodayCost;
    }

    private async Task LoadEntriesAsync()
    {
        try
        {
            var list = await _service.GetEntriesAsync(
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                FromDate,
                ToDate,
                SelectedReason,
                SelectedFilterItem?.Id);

            Entries.Clear();
            foreach (var e in list) Entries.Add(e);
        }
        catch (Exception ex)
        {
            _messages.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Load waste entries"));
        }
    }

    private async Task LoadFilterItemsAsync()
    {
        if (FilterItems.Count > 0) return; 
        var items = await _itemService.SearchAsync(null, null);
        FilterItems.Clear();
        foreach (var i in items) FilterItems.Add(i);
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync() => await LoadEntriesAsync();

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SearchText = string.Empty;
        FromDate = null;
        ToDate = null;
        SelectedReason = null;
        SelectedFilterItem = null;
        await LoadEntriesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAddWaste))]
    private async Task OpenNewEntryDialogAsync()
    {
        var activeItems = (await _itemService.SearchAsync(null, null)).Where(i => i.IsActive).ToList();
        if (!activeItems.Any())
        {
            _messages.ShowError("لا توجد أصناف نشطة في المخزون.");
            return;
        }

        var vm = new WasteEntryDialogViewModel(activeItems);
        var dialog = new WasteEntryDialog(vm, _stock);
        if (dialog.ShowDialog() == true)
        {
            var (succeeded, error) = await _service.SaveAsync(new SaveWasteEntryRequest(
                vm.SelectedItem!.Id,
                vm.SelectedItem.BaseUnitId,
                vm.Quantity,
                vm.SelectedItem.PurchasePrice,
                vm.SelectedReason!,
                vm.Notes));

            if (succeeded)
            {
                await RefreshAsync();
            }
            else
            {
                _messages.ShowError(error ?? "فشل حفظ سجل الهالك.");
            }
        }
    }

    private bool CanAddWaste() => _permissionService.HasPermission(PermissionKeys.ProductionWaste);
}


public sealed partial class WasteEntryDialogViewModel : ObservableObject
{
    public WasteEntryDialogViewModel(IReadOnlyList<ItemDto> activeItems)
    {
        ActiveItems = activeItems;
    }

    public IReadOnlyList<ItemDto> ActiveItems { get; }

    public IReadOnlyList<string> Reasons { get; } =
    [
        "تلف", "انتهاء صلاحية", "كسر", "حرق", "عيب تصنيع", "مرتجع غير صالح", "سبب آخر"
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitSymbol))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private ItemDto? selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private decimal quantity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string? selectedReason;

    [ObservableProperty] private string? notes;
    [ObservableProperty] private decimal availableStock;
    [ObservableProperty] private string? validationMessage;

    public string UnitSymbol => SelectedItem?.BaseUnit ?? string.Empty;

    public bool CanSave =>
        SelectedItem != null &&
        !string.IsNullOrWhiteSpace(SelectedReason) &&
        Quantity > 0 &&
        Quantity <= AvailableStock;

    partial void OnSelectedItemChanged(ItemDto? value)
    {
        AvailableStock = 0;
        ValidationMessage = null;
        if (value != null)
            _ = LoadStockAsync(value.Id);
    }

    partial void OnQuantityChanged(decimal value)
    {
        ValidateQuantity();
        OnPropertyChanged(nameof(CanSave));
    }

    public void ValidateQuantity()
    {
        if (Quantity <= 0)
            ValidationMessage = "الكمية يجب أن تكون أكبر من الصفر.";
        else if (Quantity > AvailableStock)
            ValidationMessage = "الكمية التالفة أكبر من الرصيد المتاح للصنف.";
        else
            ValidationMessage = null;
        OnPropertyChanged(nameof(CanSave));
    }

    private async Task LoadStockAsync(int itemId)
    {
        
        await Task.CompletedTask;
    }

    public void SetAvailableStock(decimal stock)
    {
        AvailableStock = stock;
        ValidateQuantity();
    }
}


public sealed partial class JobRolesViewModel : ViewModelBase
{
    private readonly IJobRoleService _service;
    private readonly IMessageService _messageService;
    private readonly IValidationService _validationService;
    private readonly IExceptionTranslator _exceptionTranslator;
    private readonly IPermissionService _permissionService;

    public JobRolesViewModel(IJobRoleService service, IMessageService messageService, IValidationService validationService, IExceptionTranslator exceptionTranslator, IPermissionService permissionService)
    {
        _service = service;
        _messageService = messageService;
        _validationService = validationService;
        _exceptionTranslator = exceptionTranslator;
        _permissionService = permissionService;
        Title = "وظائف العاملين";
        Roles = [];
        _ = RefreshAsync();
    }

    public ObservableCollection<JobRole> Roles { get; }
    [ObservableProperty] private JobRole? selectedRole;
    [ObservableProperty] private JobRoleStats? stats;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Roles.Clear();
        foreach (var r in await _service.GetAllRolesAsync()) Roles.Add(r);
        Stats = await _service.GetStatsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddRoleAsync()
    {
        var vm = new JobRoleFormViewModel(null, _validationService);
        var dialog = new JobRoleFormDialog(vm);
        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _service.CreateRoleAsync(vm.ToEntity());
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _messageService.ShowError(_exceptionTranslator.Translate(ex));
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditRoleAsync(JobRole role)
    {
        if (role == null) return;
        var vm = new JobRoleFormViewModel(role, _validationService);
        var dialog = new JobRoleFormDialog(vm);
        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _service.UpdateRoleAsync(vm.ToEntity());
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _messageService.ShowError(_exceptionTranslator.Translate(ex));
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteRoleAsync(JobRole role)
    {
        if (role == null) return;
        if (!await _service.CanDeleteRoleAsync(role.Id))
        {
            _messageService.ShowError("لا يمكن حذف الوظيفة لارتباطها بموظفين أو حركات سابقة. يمكنك تعطيلها بدلاً من ذلك.");
            return;
        }
        if (_messageService.Confirm($"هل أنت متأكد من حذف الوظيفة {role.Name}؟"))
        {
            await _service.DeleteRoleAsync(role.Id);
            await RefreshAsync();
        }
    }

    private bool CanAdd() => _permissionService.HasPermission(PermissionKeys.EmployeesAdd);
    private bool CanEdit() => _permissionService.HasPermission(PermissionKeys.EmployeesEdit);
    private bool CanDelete() => _permissionService.HasPermission(PermissionKeys.EmployeesDelete);
}

public sealed partial class EmployeesViewModel : ViewModelBase
{
    private readonly IEmployeeService _service;
    private readonly IJobRoleService _jobRoleService;
    private readonly IMessageService _messageService;
    private readonly IValidationService _validationService;
    private readonly IExceptionTranslator _exceptionTranslator;
    private readonly IPartyService _partyService;
    private readonly IPermissionService _permissionService;

    public EmployeesViewModel(IEmployeeService service, IJobRoleService jobRoleService, IMessageService messageService, INavigationService navigationService, IValidationService validationService, IExceptionTranslator exceptionTranslator, IPartyService partyService, IPermissionService permissionService)
    {
        _service = service;
        _jobRoleService = jobRoleService;
        _messageService = messageService;
        _navigationService = navigationService;
        _validationService = validationService;
        _exceptionTranslator = exceptionTranslator;
        _partyService = partyService;
        _permissionService = permissionService;
        Title = Loc.EmployeesView;
        Employees = [];
        _ = RefreshAsync();
    }

    private readonly INavigationService _navigationService;

    [RelayCommand]
    private void NavigateToJobRoles() => _navigationService.NavigateTo<JobRolesViewModel>();

    public ObservableCollection<Employee> Employees { get; }
    [ObservableProperty] private Employee? selectedEmployee;
    [ObservableProperty] private string searchText = string.Empty;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalWagesText))]
    private EmployeeStats? stats;

    public string TotalWagesText => _permissionService.HasPermission(PermissionKeys.EmployeesSalaries)
        ? (Stats?.MonthlyPayroll.ToString("N0") ?? "0")
        : Loc.NoPermission;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Employees.Clear();
        var list = string.IsNullOrWhiteSpace(SearchText) 
            ? await _service.GetAllEmployeesAsync() 
            : await _service.SearchEmployeesAsync(SearchText);
            
        foreach (var e in list) Employees.Add(e);
        Stats = await _service.GetEmployeeStatsAsync();
    }

    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();

    [RelayCommand(CanExecute = nameof(CanAddEmployee))]
    private async Task AddEmployeeAsync()
    {
        var roles = await _jobRoleService.GetActiveRolesAsync();
        if (!roles.Any())
        {
            _messageService.ShowError("يجب إضافة وظائف أولاً قبل إضافة الموظفين.");
            return;
        }
        var vm = new EmployeeFormViewModel(roles, _validationService);
        
        bool tryAgain = true;
        while (tryAgain)
        {
            tryAgain = false;
            var dialog = new EmployeeFormDialog(vm);
            if (dialog.ShowDialog() == true)
            {
                var dupCheck = await _partyService.CheckNameDuplicatesAsync(vm.Name);
                if (dupCheck.HasDuplicates)
                {
                    if (!_messageService.Confirm(dupCheck.WarningMessage))
                    {
                        tryAgain = true;
                        continue;
                    }
                }

                try
                {
                    await _service.CreateEmployeeAsync(vm.ToEntity());
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    _messageService.ShowError(_exceptionTranslator.Translate(ex));
                }
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditEmployee))]
    private async Task EditEmployeeAsync(Employee employee)
    {
        if (employee == null) return;
        var roles = await _jobRoleService.GetActiveRolesAsync();
        var vm = new EmployeeFormViewModel(employee, roles, _validationService);
        
        bool tryAgain = true;
        while (tryAgain)
        {
            tryAgain = false;
            var dialog = new EmployeeFormDialog(vm);
            if (dialog.ShowDialog() == true)
            {
                var dupCheck = await _partyService.CheckNameDuplicatesAsync(vm.Name, employee.PartyId);
                if (dupCheck.HasDuplicates)
                {
                    if (!_messageService.Confirm(dupCheck.WarningMessage))
                    {
                        tryAgain = true;
                        continue;
                    }
                }

                try
                {
                    await _service.UpdateEmployeeAsync(vm.ToEntity());
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    _messageService.ShowError(_exceptionTranslator.Translate(ex));
                }
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteEmployee))]
    private async Task DeleteEmployeeAsync(Employee employee)
    {
        if (employee == null) return;
        
        if (!await _service.CanDeleteEmployeeAsync(employee.Id))
        {
            _messageService.ShowError("لا يمكن حذف الموظف لوجود حركات مسجلة (أجور أو إنتاج). يمكنك إلغاء تنشيطه بدلاً من ذلك.");
            return;
        }

        if (_messageService.Confirm($"هل أنت متأكد من حذف الموظف {employee.Name}؟"))
        {
            await _service.DeleteEmployeeAsync(employee.Id);
            await RefreshAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditEmployee))]
    private async Task ToggleActiveAsync(Employee employee)
    {
        if (employee == null) return;
        employee.IsActive = !employee.IsActive;
        await _service.UpdateEmployeeAsync(employee);
        await RefreshAsync();
    }

    private bool CanAddEmployee() => _permissionService.HasPermission(PermissionKeys.EmployeesAdd);
    private bool CanEditEmployee() => _permissionService.HasPermission(PermissionKeys.EmployeesEdit);
    private bool CanDeleteEmployee() => _permissionService.HasPermission(PermissionKeys.EmployeesDelete);
}

public sealed partial class EmployeeWagesViewModel : ViewModelBase
{
    private readonly IEmployeeWageService _service;
    public EmployeeWagesViewModel(IEmployeeWageService service)
    {
        _service = service;
        Title = Loc.EmployeeWages;
        Wages = [];
        _ = RefreshAsync();
    }

    public ObservableCollection<EmployeeWage> Wages { get; }
    [ObservableProperty] private EmployeeWage? selectedWage;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Wages.Clear();
        foreach (var w in await _service.GetAllWagesAsync()) Wages.Add(w);
    }
}
