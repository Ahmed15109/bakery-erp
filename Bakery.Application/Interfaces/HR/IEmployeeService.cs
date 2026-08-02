using Bakery.Domain.Entities;

namespace Bakery.Application.Interfaces;

public record EmployeeStats(int Total, int Active, decimal MonthlyPayroll, int ProductionWorkers);

public interface IEmployeeService
{
    Task<IEnumerable<Employee>> GetAllEmployeesAsync();
    Task<IEnumerable<Employee>> SearchEmployeesAsync(string query);
    Task<Employee?> GetEmployeeByIdAsync(int id);
    Task<Employee> CreateEmployeeAsync(Employee employee);
    Task UpdateEmployeeAsync(Employee employee);
    Task DeleteEmployeeAsync(int id);
    Task<bool> CanDeleteEmployeeAsync(int id);
    Task<EmployeeStats> GetEmployeeStatsAsync();
}
