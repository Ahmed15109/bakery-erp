using Bakery.Domain.Enums;

namespace Bakery.Application.DTOs.Payroll;

public record SalarySlipDto(
    string EmployeeName,
    string RoleName,
    string PeriodName,
    DateTime StartDate,
    DateTime EndDate,
    WageType PaymentType,
    decimal BaseAmount,
    decimal AttendanceTotal,
    decimal ProductionTotal,
    decimal Bonuses,
    decimal Deductions,
    decimal Advances,
    decimal NetAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    DateTime GeneratedAt
);
