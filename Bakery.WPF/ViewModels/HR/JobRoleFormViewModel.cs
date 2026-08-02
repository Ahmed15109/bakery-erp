using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bakery.WPF.ViewModels;

public sealed partial class JobRoleFormViewModel : ObservableObject
{
    private readonly IValidationService? _validationService;
    private CancellationTokenSource? _validationCts;

    public JobRoleFormViewModel(JobRole? role = null, IValidationService? validationService = null)
    {
        _validationService = validationService;
        if (role != null)
        {
            Id = role.Id;
            Name = role.Name;
            WageType = role.WageType;
            WageAmount = role.WageAmount;
            DailyRate = role.DailyRate;
            MonthlySalary = role.MonthlySalary;
            ProductionRate = role.ProductionRate;
            IsActive = role.IsActive;
            Notes = role.Notes ?? string.Empty;
            _ = ValidateNameAsync(Name);
        }
        else
        {
            IsActive = true;
        }
    }

    public int Id { get; }
    public IEnumerable<WageType> WageTypes => Enum.GetValues<WageType>();

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private WageType wageType;
    [ObservableProperty] private decimal wageAmount;
    [ObservableProperty] private decimal dailyRate;
    [ObservableProperty] private decimal monthlySalary;
    [ObservableProperty] private decimal productionRate;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private string notes = string.Empty;

    [ObservableProperty] private bool? isNameValid;
    [ObservableProperty] private string nameValidationMessage = string.Empty;

    partial void OnNameChanged(string value)
    {
        _ = ValidateNameAsync(value);
    }

    private async Task ValidateNameAsync(string value)
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
                IsNameValid = false;
                NameValidationMessage = "اسم الوظيفة مطلوب";
            }
            else
            {
                var used = await _validationService.IsJobRoleNameUsedAsync(value, Id > 0 ? Id : null);
                IsNameValid = !used;
                NameValidationMessage = used ? "اسم الوظيفة مستخدم بالفعل" : "✅ الاسم متاح";
            }
        }
        catch (OperationCanceledException) { }
    }

    public JobRole ToEntity()
    {
        return new JobRole
        {
            Id = Id,
            Name = Name,
            WageType = WageType,
            WageAmount = WageAmount,
            DailyRate = DailyRate,
            MonthlySalary = MonthlySalary,
            ProductionRate = ProductionRate,
            IsActive = IsActive,
            Notes = Notes
        };
    }
}
