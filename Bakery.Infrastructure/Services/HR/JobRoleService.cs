using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class JobRoleService : IJobRoleService
{
    private readonly IRepository<JobRole> _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly IValidationService _validationService;
    private readonly IPermissionService _permissionService;

    public JobRoleService(IRepository<JobRole> repository, IUnitOfWork unitOfWork, IAuditService auditService, IValidationService validationService, IPermissionService permissionService)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _validationService = validationService;
        _permissionService = permissionService;
    }

    public async Task<JobRole> CreateRoleAsync(JobRole role)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        if (await _validationService.IsJobRoleNameUsedAsync(role.Name))
            throw new System.ComponentModel.DataAnnotations.ValidationException("اسم الوظيفة مستخدم بالفعل");

        var added = await _repository.AddAsync(role);
        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.Create, "JobRole", added.Id, null, added.Name);
        return added;
    }

    public async Task DeleteRoleAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        if (!await CanDeleteRoleAsync(id))
            throw new InvalidOperationException("لا يمكن حذف الوظيفة لوجود موظفين مسجلين عليها أو حركات سابقة");

        var role = await _repository.GetByIdAsync(id);
        if (role != null)
        {
            await _repository.DeleteAsync(role);
            await _unitOfWork.SaveChangesAsync();
            await _auditService.LogAsync(AuditActionKeys.Delete, "JobRole", id, null, role.Name);
        }
    }

    public async Task<bool> CanDeleteRoleAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return true;

        var hasEmployees = await context.Set<Employee>().AnyAsync(e => e.JobRoleId == id);
        // We also check snapshots in history
        var hasWageHistory = await context.Set<EmployeeWage>().AnyAsync(w => w.WageAmountSnapshot > 0 && w.Employee.JobRoleId == id);
        
        return !hasEmployees && !hasWageHistory;
    }

    public async Task<IEnumerable<JobRole>> GetAllRolesAsync()
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return await _repository.ListAsync();

        return await context.Set<JobRole>()
            .Include(r => r.Employees)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<JobRole>> GetActiveRolesAsync()
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return await _repository.ListAsync();

        return await context.Set<JobRole>().Where(r => r.IsActive).ToListAsync();
    }

    public async Task<JobRole?> GetRoleByIdAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        return await _repository.GetByIdAsync(id);
    }

    public async Task<JobRoleStats> GetStatsAsync()
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return new JobRoleStats(0, 0, 0);

        var all = await context.Set<JobRole>().ToListAsync();
        var empCount = await context.Set<Employee>().CountAsync(e => e.IsActive);

        return new JobRoleStats(all.Count, all.Count(r => r.IsActive), empCount);
    }

    public async Task UpdateRoleAsync(JobRole role)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        if (await _validationService.IsJobRoleNameUsedAsync(role.Name, role.Id))
            throw new System.ComponentModel.DataAnnotations.ValidationException("اسم الوظيفة مستخدم بالفعل");

        await _repository.UpdateAsync(role);
        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.Update, "JobRole", role.Id, null, role.Name);
    }
}
