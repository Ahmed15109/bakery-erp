using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bakery.WPF.ViewModels;

public sealed partial class EmployeeFormViewModel : ObservableObject
{
    private readonly IValidationService? _validationService;
    private CancellationTokenSource? _validationCts;
    private bool _suppressWageAutoFill;

    public EmployeeFormViewModel(IEnumerable<JobRole> roles, IValidationService? validationService = null)
    {
        JobRoles = roles;
        _validationService = validationService;
        HireDate = DateOnly.FromDateTime(DateTime.Today);
        IsActive = true;

        _ = ValidateCodeAsync(Code);
    }

    public EmployeeFormViewModel(Employee employee, IEnumerable<JobRole> roles, IValidationService? validationService = null)
    {
        JobRoles = roles;
        _validationService = validationService;
        Id = employee.Id;
        PartyId = employee.PartyId;
        Code = employee.Code;
        Name = employee.Name;
        Phone = employee.Phone ?? string.Empty;
        Address = employee.Address ?? string.Empty;
        NationalId = employee.NationalId ?? string.Empty;
        HireDate = employee.HireDate;
        IsActive = employee.IsActive;
        Notes = employee.Notes ?? string.Empty;

        _suppressWageAutoFill = true;
        WageType = employee.WageType;
        MonthlySalary = employee.MonthlySalary;
        DailyRate = employee.DailyRate;
        ProductionRate = employee.ProductionRate;
        WageEffectiveFrom = employee.WageEffectiveFrom == default
            ? DateOnly.FromDateTime(DateTime.Today)
            : employee.WageEffectiveFrom;
        _suppressWageAutoFill = false;

        SelectedJobRole = roles.FirstOrDefault(r => r.Id == employee.JobRoleId);

        IsCodeValid = true; 

        _ = ValidateCodeAsync(Code);
    }

    public int Id { get; }
    public int PartyId { get; }
    public IEnumerable<JobRole> JobRoles { get; }
    public IEnumerable<WageType> WageTypes => Enum.GetValues<WageType>();

    [ObservableProperty] private string code = string.Empty;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string phone = string.Empty;
    [ObservableProperty] private string address = string.Empty;
    [ObservableProperty] private string nationalId = string.Empty;
    [ObservableProperty] private JobRole? selectedJobRole;
    [ObservableProperty] private DateOnly hireDate;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private string notes = string.Empty;

    [ObservableProperty] private WageType wageType = WageType.Production;
    [ObservableProperty] private decimal monthlySalary;
    [ObservableProperty] private decimal dailyRate;
    [ObservableProperty] private decimal productionRate;
    [ObservableProperty] private DateOnly wageEffectiveFrom;

    public bool CanSave => !string.IsNullOrWhiteSpace(Name) && 
                           !string.IsNullOrWhiteSpace(Code) && 
                           SelectedJobRole != null && 
                           IsCodeValid == true;

    public bool IsMonthlyWage => WageType == WageType.Monthly;
    public bool IsDailyWage => WageType == WageType.Daily;
    public bool IsProductionWage => WageType == WageType.Production;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(CanSave));

    partial void OnWageTypeChanged(WageType value)
    {
        OnPropertyChanged(nameof(IsMonthlyWage));
        OnPropertyChanged(nameof(IsDailyWage));
        OnPropertyChanged(nameof(IsProductionWage));
    }

    partial void OnSelectedJobRoleChanged(JobRole? value)
    {
        OnPropertyChanged(nameof(CanSave));

        if (_suppressWageAutoFill || value == null) return;

        if (Id == 0)
        {
            WageType = value.WageType;
            MonthlySalary = value.MonthlySalary;
            DailyRate = value.DailyRate;
            ProductionRate = value.ProductionRate;
            WageEffectiveFrom = HireDate == default
                ? DateOnly.FromDateTime(DateTime.Today)
                : HireDate;
        }
    }

    partial void OnHireDateChanged(DateOnly value)
    {
        if (Id == 0)
            WageEffectiveFrom = value;
    }

    [ObservableProperty] private bool? isCodeValid;
    [ObservableProperty] private string codeValidationMessage = string.Empty;

    partial void OnIsCodeValidChanged(bool? value) => OnPropertyChanged(nameof(CanSave));

    partial void OnCodeChanged(string value)
    {
        _ = ValidateCodeAsync(value);
        OnPropertyChanged(nameof(CanSave));
    }

    private async Task ValidateCodeAsync(string value)
    {
        if (_validationService == null) return;

        _validationCts?.Cancel();
        _validationCts = new CancellationTokenSource();
        var token = _validationCts.Token;

        try
        {
            await Task.Delay(300, token);
            if (string.IsNullOrWhiteSpace(value))
            {
                IsCodeValid = false;
                CodeValidationMessage = "كود الموظف مطلوب";
            }
            else
            {
                var used = await _validationService.IsEmployeeCodeUsedAsync(value, Id > 0 ? Id : null);
                IsCodeValid = !used;
                CodeValidationMessage = used ? "كود الموظف مستخدم بالفعل" : " الكود متاح";
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public Employee ToEntity()
    {
        return new Employee
        {
            Id = Id,
            PartyId = PartyId,
            Code = Code,
            Name = Name,
            Phone = Phone,
            Address = Address,
            NationalId = NationalId,
            JobRoleId = SelectedJobRole?.Id ?? 0,
            HireDate = HireDate,
            IsActive = IsActive,
            Notes = Notes,

            WageType = WageType,
            MonthlySalary = MonthlySalary,
            DailyRate = DailyRate,
            ProductionRate = ProductionRate,
            WageEffectiveFrom = WageEffectiveFrom == default
                ? DateOnly.FromDateTime(DateTime.Today)
                : WageEffectiveFrom,
        };
    }
}
