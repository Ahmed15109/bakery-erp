using Bakery.Domain.Entities;

namespace Bakery.Application.Interfaces;

public record JobRoleStats(int TotalRoles, int ActiveRoles, int TotalEmployeesAssigned);

public interface IJobRoleService
{
    Task<IEnumerable<JobRole>> GetAllRolesAsync();
    Task<IEnumerable<JobRole>> GetActiveRolesAsync();
    Task<JobRole?> GetRoleByIdAsync(int id);
    Task<JobRole> CreateRoleAsync(JobRole role);
    Task UpdateRoleAsync(JobRole role);
    Task DeleteRoleAsync(int id);
    Task<bool> CanDeleteRoleAsync(int id);
    Task<JobRoleStats> GetStatsAsync();
}
