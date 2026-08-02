using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Bakery.Infrastructure.Services;

public sealed class EmployeeStatementProvider : IEmployeeStatementProvider
{
    private readonly BakeryDbContext _db;
    private readonly IPermissionService _permissionService;

    public EmployeeStatementProvider(BakeryDbContext db, IPermissionService permissionService)
    {
        _db = db;
        _permissionService = permissionService;
    }

    public async Task<IReadOnlyList<PartyStatementLineDto>> GetStatementAsync(int employeeId, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        Debug.WriteLine($"[EmployeeStatementProvider] GetStatementAsync called with employeeId={employeeId}");
        
        var transactions = await _db.Set<EmployeeTransaction>()
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        Debug.WriteLine($"[EmployeeStatementProvider] DB returned {transactions.Count} EmployeeTransaction rows for employeeId={employeeId}");
        decimal runningBalance = 0;
        var lines = new List<PartyStatementLineDto>();

        foreach (var tx in transactions)
        {
            decimal debit = 0;   // مدين (سحب سلفة / صرف)
            decimal credit = 0;  // دائن (استحقاق أجور / مكافأة)

            switch (tx.Type)
            {
                case EmployeeTransactionType.Earned:
                case EmployeeTransactionType.Bonus:
                    credit = tx.Amount;
                    runningBalance += tx.Amount;
                    break;
                case EmployeeTransactionType.Advance:
                case EmployeeTransactionType.SalaryPayment:
                case EmployeeTransactionType.Deduction:
                    debit = tx.Amount;
                    runningBalance -= tx.Amount;
                    break;
            }

            // صياغة البيان باللغة العربية
            string description = tx.Type switch
            {
                EmployeeTransactionType.Earned => tx.Notes ?? "أجر مستحق",
                EmployeeTransactionType.Advance => "سحب مقدم / سلفة",
                EmployeeTransactionType.Bonus => tx.Notes ?? "مكافأة تشجيعية",
                EmployeeTransactionType.Deduction => tx.Notes ?? "خصومات إدارية",
                EmployeeTransactionType.SalaryPayment => "صرف مستحقات الراتب",
                _ => tx.Type.ToString()
            };

            lines.Add(new PartyStatementLineDto(
                tx.Date,
                description,
                credit,             // Increase = Credit (earned/bonus)
                debit,              // Decrease = Debit (advance/payment/deduction)
                tx.Amount,          // Remaining
                runningBalance,     // RunningBalance
                "EmployeeTransaction",
                tx.Id
            ));
        }

        return lines;
    }
}
