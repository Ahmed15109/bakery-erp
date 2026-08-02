using Bakery.Domain.Enums;
using Bakery.Domain.Interfaces;
using System;
using System.Collections.Generic;

namespace Bakery.Domain.Entities;

public sealed class WasteEntry : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int WorkingDayId { get; set; }
    public WorkingDay WorkingDay { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal WasteCost { get; set; }
    public string Reason { get; set; } = string.Empty;
    public WasteType WasteType { get; set; }
    public string? Notes { get; set; }
}

public sealed class Expense : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int WorkingDayId { get; set; }
    public WorkingDay WorkingDay { get; set; } = null!;
    public int SafeId { get; set; }
    public Safe Safe { get; set; } = null!;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class JobRole : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public WageType WageType { get; set; }
    public decimal WageAmount { get; set; }
    public decimal DailyRate { get; set; }
    public decimal MonthlySalary { get; set; }
    public decimal ProductionRate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public ICollection<Employee> Employees { get; set; } = [];
}

public sealed class Employee : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public int PartyId { get; set; }
    public Party Party { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? NationalId { get; set; }
    public int JobRoleId { get; set; }
    public JobRole JobRole { get; set; } = null!;
    public DateOnly HireDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public WageType WageType { get; set; } = WageType.Production;
    public decimal MonthlySalary { get; set; }
    public decimal DailyRate { get; set; }
    public decimal ProductionRate { get; set; }
    public DateOnly WageEffectiveFrom { get; set; }
    public DateTime? WageLastUpdatedAt { get; set; }
    public string? WageLastUpdatedBy { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal CurrentWageAmount
    {
        get
        {
            return WageType switch
            {
                WageType.Monthly => MonthlySalary,
                WageType.Daily => DailyRate,
                WageType.Production => ProductionRate,
                WageType.Piecework => ProductionRate,
                _ => 0m
            };
        }
    }

    public ICollection<EmployeeWage> Wages { get; set; } = [];
    public ICollection<Attendance> Attendances { get; set; } = [];
}

public sealed class Attendance : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int WorkingDayId { get; set; }
    public WorkingDay WorkingDay { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateTime CheckIn { get; set; }
    public DateTime? CheckOut { get; set; }
    public bool IsPresent { get; set; } = true;
    public string? Notes { get; set; }
}

public sealed class EmployeeWage : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int WorkingDayId { get; set; }
    public WorkingDay WorkingDay { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int? SafeId { get; set; }
    public Safe? Safe { get; set; }
    public DateOnly WageDate { get; set; }
    public decimal Amount { get; set; }
    public WageType WageTypeSnapshot { get; set; }
    public decimal WageAmountSnapshot { get; set; }
    public string? Notes { get; set; }
    public int? ReversalReferenceId { get; set; }
    public bool IsReversed { get; set; }
}

public sealed class AuditLog : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? MachineName { get; set; }
    public string? IPAddress { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public sealed class AppSetting : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class PayrollPeriod : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;
    public decimal TotalNetAmount { get; set; }
    public string? Notes { get; set; }
    public ICollection<EmployeeSettlement> Entries { get; set; } = [];
}

public sealed class EmployeeSettlement : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int? PayrollPeriodId { get; set; }
    public PayrollPeriod? PayrollPeriod { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateTime SettlementDate { get; set; } = DateTime.UtcNow;
    public WageType WageTypeSnapshot { get; set; }
    public decimal ProductionQuantity { get; set; }
    public decimal ProductionRate { get; set; }
    public decimal DailyRate { get; set; }
    public decimal AttendanceCount { get; set; }
    public decimal MonthlySalary { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal Bonuses { get; set; }
    public decimal Deductions { get; set; }
    public decimal Advances { get; set; }
    public decimal NetAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public bool IsFullyPaid { get; set; }
    public string? Notes { get; set; }
    public ICollection<EmployeeTransaction> Transactions { get; set; } = [];
}

public sealed class EmployeeTransaction : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int? WorkingDayId { get; set; }
    public WorkingDay? WorkingDay { get; set; }
    public EmployeeTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public int? SettlementId { get; set; }
    public EmployeeSettlement? Settlement { get; set; }
    public int? SafeMovementId { get; set; }
    public SafeMovement? SafeMovement { get; set; }
}
