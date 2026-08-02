using System.Collections.ObjectModel;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class SettlementViewModel : ViewModelBase, IDisposable
{
    private readonly ISettlementService _settlementService;
    private readonly IEmployeeService _employeeService;
    private readonly ISafeService _safeService;
    private readonly IWorkingDayService _workingDayService;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IExceptionTranslator _exceptionTranslator;
    private readonly IPermissionService _permissionService;
    private readonly ISafeContext _safeContext;
    private readonly EventHandler<SafeChangedEventArgs> _safeChangedHandler;
    private bool _isInitializing;

    public SettlementViewModel(
        ISettlementService settlementService,
        IEmployeeService employeeService,
        ISafeService safeService,
        IWorkingDayService workingDayService,
        IMessageService messageService,
        IDialogService dialogService,
        IExceptionTranslator exceptionTranslator,
        IPermissionService permissionService,
        ISafeContext safeContext)
    {
        _settlementService = settlementService;
        _employeeService = employeeService;
        _safeService = safeService;
        _workingDayService = workingDayService;
        _messageService = messageService;
        _dialogService = dialogService;
        _exceptionTranslator = exceptionTranslator;
        _permissionService = permissionService;
        _safeContext = safeContext;
        Title = "تسوية مستحقات الموظفين";
        SettlementDate = DateTime.Today;

        _safeChangedHandler = (_, args) =>
        {
            _ = SyncWithActiveSafeAsync();
        };
        _safeContext.SafeChanged += _safeChangedHandler;
        
        LoadDataCommand.Execute(null);
    }

    public void Dispose()
    {
        _safeContext.SafeChanged -= _safeChangedHandler;
    }

    public bool HasSalariesPermission => _permissionService.HasPermission(PermissionKeys.EmployeesSalaries);
    public bool HasAdvancesPermission => _permissionService.HasPermission(PermissionKeys.EmployeesAdvances);

    [ObservableProperty] private ObservableCollection<Employee> employees = [];
    [ObservableProperty] private ObservableCollection<SafeDto> safes = [];
    [ObservableProperty] private Employee? selectedEmployee;
    [ObservableProperty] private SafeDto? selectedSafe;
    [ObservableProperty] private bool canChangeSafe;
    [ObservableProperty] private DateTime settlementDate;

    // Wage Type State
    [ObservableProperty] private WageType currentWageType;
    [ObservableProperty] private bool isProductionType;
    [ObservableProperty] private bool isDailyType;
    [ObservableProperty] private bool isMonthlyType;

    // Settlement Form
    [ObservableProperty] private decimal quantity;
    [ObservableProperty] private decimal rate;
    [ObservableProperty] private decimal attendanceCount = 1;
    [ObservableProperty] private decimal dailyRate;
    [ObservableProperty] private decimal monthlySalary;

    [ObservableProperty] private decimal grossAmount;
    [ObservableProperty] private decimal bonus;
    [ObservableProperty] private decimal deduction;
    [ObservableProperty] private decimal advance;
    [ObservableProperty] private decimal netToPay;
    [ObservableProperty] private decimal remainingBalance; // Live update
    [ObservableProperty] private decimal currentSafeBalance;
    [ObservableProperty] private bool isInsufficientFunds;
    [ObservableProperty] private string balanceWarning = string.Empty;
    [ObservableProperty] private string notes = string.Empty;

    // Side Panel
    [ObservableProperty] private decimal currentBalance;
    [ObservableProperty] private ObservableCollection<EmployeeTransaction> lastTransactions = [];

    partial void OnQuantityChanged(decimal value) => CalculateTotals();
    partial void OnRateChanged(decimal value) => CalculateTotals();
    partial void OnAttendanceCountChanged(decimal value) => CalculateTotals();
    partial void OnDailyRateChanged(decimal value) => CalculateTotals();
    partial void OnMonthlySalaryChanged(decimal value) => CalculateTotals();
    partial void OnBonusChanged(decimal value) => CalculateTotals();
    partial void OnDeductionChanged(decimal value) => CalculateTotals();
    partial void OnAdvanceChanged(decimal value) => CalculateTotals();

    private void CalculateTotals()
    {
        switch (CurrentWageType)
        {
            case WageType.Production:
                GrossAmount = Quantity * Rate;
                break;
            case WageType.Daily:
                GrossAmount = AttendanceCount * DailyRate;
                break;
            case WageType.Monthly:
                GrossAmount = MonthlySalary;
                break;
        }

        NetToPay = GrossAmount + Bonus - Deduction;
        RemainingBalance = CurrentBalance + NetToPay - Advance;

        UpdateBalanceInfo();
    }

    private void UpdateBalanceInfo()
    {
        if (SelectedSafe != null && Advance > CurrentSafeBalance)
        {
            IsInsufficientFunds = true;
            decimal deficit = Advance - CurrentSafeBalance;
            BalanceWarning = $"رصيد الخزنة غير كافٍ! المتاح: {CurrentSafeBalance:N2} ج.م | العجز: {deficit:N2} ج.م";
        }
        else
        {
            IsInsufficientFunds = false;
            BalanceWarning = string.Empty;
        }
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        _isInitializing = true;
        try
        {
            var emps = await _employeeService.GetAllEmployeesAsync();
            Employees = new ObservableCollection<Employee>(emps.Where(e => e.IsActive));
            
            var sfs = await _safeService.ListSafesAsync();
            Safes = new ObservableCollection<SafeDto>(sfs);
            
            if (_safeContext.CurrentSafeId.HasValue)
            {
                SelectedSafe = Safes.FirstOrDefault(s => s.Id == _safeContext.CurrentSafeId.Value);
                CanChangeSafe = false;
            }
            else
            {
                var defaultSafeId = (await _safeService.GetDefaultCashSafeAsync()).Id;
                SelectedSafe = Safes.FirstOrDefault(s => s.Id == defaultSafeId) ?? Safes.FirstOrDefault();
                CanChangeSafe = true;
            }
        }
        finally
        {
            _isInitializing = false;
        }

        if (SelectedSafe != null) 
        {
            await UpdateSafeBalanceAsync(SelectedSafe.Id);
        }
    }

    private async Task SyncWithActiveSafeAsync()
    {
        if (_safeContext.CurrentSafeId.HasValue)
        {
            var activeSafeId = _safeContext.CurrentSafeId.Value;
            var match = Safes.FirstOrDefault(s => s.Id == activeSafeId);
            if (match == null)
            {
                var sfs = await _safeService.ListSafesAsync();
                Safes.Clear();
                foreach (var s in sfs) Safes.Add(s);
                match = Safes.FirstOrDefault(s => s.Id == activeSafeId);
            }
            SelectedSafe = match;
            CanChangeSafe = false;
        }
        else
        {
            CanChangeSafe = true;
        }
    }

    partial void OnSelectedSafeChanged(SafeDto? value)
    {
        if (_isInitializing) return;
        if (value != null) _ = UpdateSafeBalanceAsync(value.Id);
        else CurrentSafeBalance = 0;
    }

    private async Task UpdateSafeBalanceAsync(int safeId)
    {
        CurrentSafeBalance = await _safeService.GetBalanceAsync(safeId);
        UpdateBalanceInfo();
    }

    partial void OnSelectedEmployeeChanged(Employee? value)
    {
        if (_isInitializing) return;

        if (value != null)
        {
            CurrentWageType = value.WageType;
            IsProductionType = CurrentWageType == WageType.Production;
            IsDailyType = CurrentWageType == WageType.Daily;
            IsMonthlyType = CurrentWageType == WageType.Monthly;

            Rate = value.ProductionRate;
            DailyRate = value.DailyRate;
            MonthlySalary = value.MonthlySalary;

            LoadEmployeeStatsCommand.Execute(value.Id);
        }
        else
        {
            CurrentBalance = 0;
            RemainingBalance = 0;
            LastTransactions.Clear();
            IsProductionType = IsDailyType = IsMonthlyType = false;
        }
        
        CalculateTotals();
        ShowLedgerCommand.NotifyCanExecuteChanged();
    }


    [RelayCommand]
    private async Task LoadEmployeeStatsAsync(int employeeId)
    {
        CurrentBalance = await _settlementService.GetEmployeeBalanceAsync(employeeId);
        var txs = await _settlementService.GetEmployeeStatementAsync(employeeId);
        LastTransactions = new ObservableCollection<EmployeeTransaction>(txs.OrderByDescending(t => t.Date).Take(5));
        CalculateTotals();
    }

    [RelayCommand]
    private async Task SaveSettlementAsync()
    {
        if (SelectedEmployee == null)
        {
            _messageService.ShowError("يرجى اختيار الموظف");
            return;
        }

        if (GrossAmount <= 0 && Bonus <= 0 && Deduction <= 0 && Advance <= 0)
        {
            _messageService.ShowError("لا توجد بيانات تسوية لإدخالها");
            return;
        }

        if (GrossAmount > 0 || Bonus > 0 || Deduction > 0)
        {
            if (!HasSalariesPermission)
            {
                _messageService.ShowError("غير مصرح: ليس لديك صلاحية احتساب الرواتب والأجور.");
                return;
            }
        }

        if (Advance > 0)
        {
            if (!HasAdvancesPermission)
            {
                _messageService.ShowError("غير مصرح: ليس لديك صلاحية صرف السلف.");
                return;
            }
        }

        try
        {
            if (Advance > 0)
            {
                await _workingDayService.EnsureActiveWorkingDayAsync();
            }

            var settlement = new EmployeeSettlement
            {
                EmployeeId = SelectedEmployee.Id,
                SettlementDate = SettlementDate,
                WageTypeSnapshot = CurrentWageType,
                
                
                ProductionQuantity = Quantity,
                ProductionRate = Rate,
                
                DailyRate = DailyRate,
                AttendanceCount = AttendanceCount,

                MonthlySalary = MonthlySalary,

                BaseAmount = GrossAmount,
                Bonuses = Bonus,
                Deductions = Deduction,
                Advances = Advance,
                Notes = Notes
            };

            await _settlementService.RecordSettlementAsync(settlement, SelectedSafe?.Id);
            
            _messageService.ShowInfo("تم حفظ التسوية بنجاح.");
            await LoadEmployeeStatsAsync(SelectedEmployee.Id);
            ClearForm();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error saving settlement for employee {EmployeeId}", SelectedEmployee.Id);
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(CanShowLedger))]
    private async Task ShowLedgerAsync()
    {
        if (SelectedEmployee == null) return;

        await _dialogService.ShowDialogAsync<EmployeeLedgerViewModel>(async vm =>
            await vm.InitializeAsync(SelectedEmployee));
        
        await LoadEmployeeStatsAsync(SelectedEmployee.Id);
    }

    private bool CanShowLedger() => SelectedEmployee != null;

    private void ClearForm()
    {
        Quantity = 0;
        Bonus = 0;
        Deduction = 0;
        Advance = 0;
        Notes = string.Empty;
    }
}
