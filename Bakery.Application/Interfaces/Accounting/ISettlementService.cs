using Bakery.Domain.Entities;
using Bakery.Domain.Enums;

namespace Bakery.Application.Interfaces;

public interface ISettlementService
{
    // Settlement Core
    Task<EmployeeSettlement> RecordSettlementAsync(EmployeeSettlement settlement, int? safeId = null);
    Task<EmployeeSettlement?> GetSettlementAsync(int id);
    Task<IEnumerable<EmployeeSettlement>> GetEmployeeSettlementsAsync(int employeeId, DateTime? start = null, DateTime? end = null);

    // Ledger / Statement
    Task<IEnumerable<EmployeeTransaction>> GetEmployeeStatementAsync(int employeeId, DateTime? start = null, DateTime? end = null);
    Task<decimal> GetEmployeeBalanceAsync(int employeeId);

    // Adjustments
    Task<EmployeeTransaction> AddTransactionAsync(EmployeeTransaction transaction, int? safeId = null);
}
