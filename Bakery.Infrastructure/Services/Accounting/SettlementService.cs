using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Bakery.Infrastructure.Services;

public sealed class SettlementService : ISettlementService
{
    private readonly IRepository<EmployeeSettlement> _settlementRepository;
    private readonly IRepository<EmployeeTransaction> _transactionRepository;
    private readonly ISafeService _safeService;
    private readonly IWorkingDayService _workingDayService;
    private readonly IPermissionService _permissionService;

    public SettlementService(
        IRepository<EmployeeSettlement> settlementRepository,
        IRepository<EmployeeTransaction> transactionRepository,
        ISafeService safeService,
        IWorkingDayService workingDayService,
        IPermissionService permissionService)
    {
        _settlementRepository = settlementRepository;
        _transactionRepository = transactionRepository;
        _safeService = safeService;
        _workingDayService = workingDayService;
        _permissionService = permissionService;
    }

    public async Task<EmployeeSettlement> RecordSettlementAsync(EmployeeSettlement settlement, int? safeId = null)
    {
        if (settlement.BaseAmount > 0 || settlement.Bonuses > 0 || settlement.Deductions > 0)
        {
            _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        }
        if (settlement.Advances > 0)
        {
            _permissionService.EnsurePermission(PermissionKeys.EmployeesAdvances);
        }
        if (settlement.BaseAmount == 0 && settlement.Bonuses == 0 && settlement.Deductions == 0 && settlement.Advances == 0)
        {
            if (!_permissionService.HasPermission(PermissionKeys.EmployeesManagePayroll) && 
                !_permissionService.HasPermission(PermissionKeys.EmployeesAdvances))
            {
                _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
            }
        }
        var workingDay = await _workingDayService.EnsureActiveWorkingDayAsync();
        var context = GetContext();
        
        using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            // Calculate Base Amount based on Wage Type
            switch (settlement.WageTypeSnapshot)
            {
                case WageType.Production:
                    settlement.BaseAmount = settlement.ProductionQuantity * settlement.ProductionRate;
                    break;
                case WageType.Daily:
                    settlement.BaseAmount = settlement.AttendanceCount * settlement.DailyRate;
                    break;
                case WageType.Monthly:
                    settlement.BaseAmount = settlement.MonthlySalary;
                    break;
            }

            // Net = Earned + Bonuses - Deductions
            settlement.NetAmount = settlement.BaseAmount + settlement.Bonuses - settlement.Deductions;
            
            // Note: Advances here refers to money the employee takes *now* as part of this settlement
            // It's essentially a payment.
            settlement.PaidAmount = settlement.Advances;
            
            // Get current balance before this settlement to calculate remaining amount correctly
            var currentBalance = await CalculateEmployeeBalanceAsync(settlement.EmployeeId);
            settlement.RemainingAmount = currentBalance + settlement.NetAmount - settlement.PaidAmount;
            
            settlement.IsFullyPaid = settlement.RemainingAmount <= 0;

            settlement.Transactions = new List<EmployeeTransaction>();

            // 1. Record Earned/Net adjustment (Credit to Employee)
            // We record the Net (Earned + Bonus - Deduction) as one "Earned" entry or separate?
            // User requested Formula: Net = Earned + Bonuses - Deductions - Advances
            // So we should record these as separate transactions for the ledger.

            if (settlement.BaseAmount > 0)
            {
                settlement.Transactions.Add(new EmployeeTransaction
                {
                    EmployeeId = settlement.EmployeeId,
                    Type = EmployeeTransactionType.Earned,
                    Amount = settlement.BaseAmount,
                    Date = settlement.SettlementDate,
                    WorkingDayId = workingDay?.Id,
                    Notes = GetEarnedNotes(settlement)
                });
            }

            if (settlement.Bonuses > 0)
            {
                settlement.Transactions.Add(new EmployeeTransaction
                {
                    EmployeeId = settlement.EmployeeId,
                    Type = EmployeeTransactionType.Bonus,
                    Amount = settlement.Bonuses,
                    Date = settlement.SettlementDate,
                    WorkingDayId = workingDay?.Id,
                    Notes = "مكافأة تشجيعية"
                });
            }

            if (settlement.Deductions > 0)
            {
                settlement.Transactions.Add(new EmployeeTransaction
                {
                    EmployeeId = settlement.EmployeeId,
                    Type = EmployeeTransactionType.Deduction,
                    Amount = settlement.Deductions,
                    Date = settlement.SettlementDate,
                    WorkingDayId = workingDay?.Id,
                    Notes = "خصومات إدارية"
                });
            }

            // 2. Record Advance/Payment (Debit from Employee)
            if (settlement.PaidAmount > 0)
            {
                var paymentTx = new EmployeeTransaction
                {
                    EmployeeId = settlement.EmployeeId,
                    Type = EmployeeTransactionType.SalaryPayment,
                    Amount = settlement.PaidAmount,
                    Date = settlement.SettlementDate,
                    WorkingDayId = workingDay?.Id,
                    Notes = "صرفة نقدية عند التسوية"
                };

                if (safeId.HasValue)
                {
                    await _safeService.WithdrawAsync(
                        safeId.Value, 
                        settlement.PaidAmount, 
                        $"صرف لموظف ID:{settlement.EmployeeId} - تسوية",
                        SafeMovementType.WagePayment);
                }

                settlement.Transactions.Add(paymentTx);
            }

            await _settlementRepository.AddAsync(settlement);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            
            return settlement;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<EmployeeSettlement?> GetSettlementAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        return await GetContext().Set<EmployeeSettlement>()
            .Include(s => s.Employee)
            .Include(s => s.Transactions)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<IEnumerable<EmployeeSettlement>> GetEmployeeSettlementsAsync(int employeeId, DateTime? start = null, DateTime? end = null)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        var query = GetContext().Set<EmployeeSettlement>().Where(s => s.EmployeeId == employeeId);
        if (start.HasValue) query = query.Where(s => s.SettlementDate >= start.Value);
        if (end.HasValue) query = query.Where(s => s.SettlementDate <= end.Value);
        return await query.OrderByDescending(s => s.SettlementDate).ToListAsync();
    }

    public async Task<IEnumerable<EmployeeTransaction>> GetEmployeeStatementAsync(int employeeId, DateTime? start = null, DateTime? end = null)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        var query = GetContext().Set<EmployeeTransaction>().Where(t => t.EmployeeId == employeeId);
        if (start.HasValue) query = query.Where(t => t.Date >= start.Value);
        if (end.HasValue) query = query.Where(t => t.Date <= end.Value);
        return await query.OrderBy(t => t.Date).ToListAsync();
    }

    public async Task<decimal> GetEmployeeBalanceAsync(int employeeId)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        return await CalculateEmployeeBalanceAsync(employeeId);
    }

    private async Task<decimal> CalculateEmployeeBalanceAsync(int employeeId)
    {
        var transactions = await GetContext().Set<EmployeeTransaction>()
            .Where(t => t.EmployeeId == employeeId)
            .ToListAsync();

        decimal balance = 0;
        foreach (var tx in transactions)
        {
            if (tx.Type == EmployeeTransactionType.Earned || tx.Type == EmployeeTransactionType.Bonus)
                balance += tx.Amount;
            else
                balance -= tx.Amount;
        }
        return balance;
    }

    public async Task<EmployeeTransaction> AddTransactionAsync(EmployeeTransaction tx, int? safeId = null)
    {
        if (tx.Type == EmployeeTransactionType.Advance || tx.Type == EmployeeTransactionType.SalaryPayment)
        {
            _permissionService.EnsurePermission(PermissionKeys.EmployeesAdvances);
        }
        else
        {
            _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        }
        await _workingDayService.EnsureActiveWorkingDayAsync();
        var context = GetContext();
        using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            if (safeId.HasValue && tx.Amount > 0)
            {
                var movementType = (tx.Type == EmployeeTransactionType.Advance || tx.Type == EmployeeTransactionType.SalaryPayment) 
                    ? SafeMovementType.WagePayment 
                    : SafeMovementType.ExpensePayment;

                await _safeService.WithdrawAsync(safeId.Value, tx.Amount, tx.Notes ?? "Employee Transaction", movementType);
            }

            if (!tx.WorkingDayId.HasValue)
            {
                var day = await _workingDayService.EnsureActiveWorkingDayAsync();
                tx.WorkingDayId = day.Id;
            }

            await _transactionRepository.AddAsync(tx);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return tx;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private string GetEarnedNotes(EmployeeSettlement s)
    {
        return s.WageTypeSnapshot switch
        {
            WageType.Production => $"إنتاج: {s.ProductionQuantity} × {s.ProductionRate}",
            WageType.Daily => $"يومية: {s.AttendanceCount} × {s.DailyRate}",
            WageType.Monthly => $"راتب شهري",
            _ => "استحقاق عمل"
        };
    }

    private DbContext GetContext()
    {
        return ((dynamic)_settlementRepository).DbContext;
    }
}
