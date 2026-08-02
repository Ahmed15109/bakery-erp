using Bakery.Domain.Entities;

namespace Bakery.Application.Interfaces;

public interface IEmployeeWageService
{
    Task<IEnumerable<EmployeeWage>> GetAllWagesAsync();
    Task<EmployeeWage?> GetWageByIdAsync(int id);
    Task<EmployeeWage> CreateWageAsync(EmployeeWage wage);
    Task UpdateWageAsync(EmployeeWage wage);
    Task DeleteWageAsync(int id);
}
