using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class EmployeeService : IEmployeeService
{
    private readonly IRepository<Employee> _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly IRepository<Party> _partyRepository;
    private readonly IValidationService _validationService;
    private readonly IPermissionService _permissionService;

    public EmployeeService(
        IRepository<Employee> repository, 
        IUnitOfWork unitOfWork, 
        IAuditService auditService,
        IRepository<Party> partyRepository,
        IValidationService validationService,
        IPermissionService permissionService)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _partyRepository = partyRepository;
        _validationService = validationService;
        _permissionService = permissionService;
    }

    public async Task<Employee> CreateEmployeeAsync(Employee employee)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesAdd);
        _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        if (string.IsNullOrWhiteSpace(employee.Name))
            throw new System.ComponentModel.DataAnnotations.ValidationException("اسم الموظف مطلوب");

        if (string.IsNullOrWhiteSpace(employee.Code))
            throw new System.ComponentModel.DataAnnotations.ValidationException("كود الموظف مطلوب");

        if (employee.JobRoleId <= 0)
            throw new System.ComponentModel.DataAnnotations.ValidationException("يجب تحديد وظيفة / دور صالح للموظف");

        if (await _validationService.IsEmployeeCodeUsedAsync(employee.Code))
            throw new System.ComponentModel.DataAnnotations.ValidationException("كود الموظف مستخدم بالفعل");

        if (employee.PartyId == 0)
        {
            var party = new Party
            {
                Name = employee.Name,
                Type = PartyType.Employee,
                Phone = employee.Phone,
                Address = employee.Address,
                NationalId = employee.NationalId
            };
            await _partyRepository.AddAsync(party);
            employee.Party = party;
        }

        // Ensure WageEffectiveFrom is set (defaults to HireDate)
        if (employee.WageEffectiveFrom == default)
            employee.WageEffectiveFrom = employee.HireDate == default
                ? DateOnly.FromDateTime(DateTime.Today)
                : employee.HireDate;

        var added = await _repository.AddAsync(employee);
        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.Create, "Employee", added.Id, null, added.Name);
        return added;
    }

    public async Task DeleteEmployeeAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesDelete);
        if (!await CanDeleteEmployeeAsync(id))
            throw new InvalidOperationException("لا يمكن حذف الموظف لوجود حركات مسجلة باسمه (أجور، إنتاج، أو قيود مالية)");

        var employee = await _repository.GetByIdAsync(id);
        if (employee != null)
        {
            await _repository.DeleteAsync(employee);
            var party = await _partyRepository.GetByIdAsync(employee.PartyId);
            if (party != null)
            {
                party.IsActive = false;
                await _partyRepository.UpdateAsync(party);
            }
            await _unitOfWork.SaveChangesAsync();
            await _auditService.LogAsync(AuditActionKeys.Delete, "Employee", id, null, employee.Name);
        }
    }

    public async Task<bool> CanDeleteEmployeeAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesDelete);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return true;

        var employee = await context.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (employee == null) return true;

        var hasWages = await context.Set<EmployeeWage>().AnyAsync(w => w.EmployeeId == id);
        var hasProduction = await context.Set<ProductionOrderEmployee>().AnyAsync(p => p.EmployeeId == id);
        var hasAttendance = await context.Set<Attendance>().AnyAsync(a => a.EmployeeId == id);
        var hasLedgerEntries = await context.Set<PartyLedgerEntry>().AnyAsync(l => l.PartyId == employee.PartyId);

        return !hasWages && !hasProduction && !hasAttendance && !hasLedgerEntries;
    }

    public async Task<IEnumerable<Employee>> GetAllEmployeesAsync()
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesView);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return await _repository.ListAsync();

        var employees = await context.Set<Employee>()
            .AsNoTracking()
            .Include(e => e.Party)
            .Include(e => e.JobRole)
            .OrderBy(e => e.Name)
            .ToListAsync();
        return RedactCompensationIfRequired(employees);
    }

    public async Task<IEnumerable<Employee>> SearchEmployeesAsync(string query)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesView);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return await GetAllEmployeesAsync();

        if (string.IsNullOrWhiteSpace(query)) return await GetAllEmployeesAsync();

        var employees = await context.Set<Employee>()
            .AsNoTracking()
            .Include(e => e.Party)
            .Include(e => e.JobRole)
            .Where(e => e.Name.Contains(query) || 
                        (e.Phone != null && e.Phone.Contains(query)) || 
                        (e.JobRole.Name.Contains(query)))
            .OrderBy(e => e.Name)
            .ToListAsync();
        return RedactCompensationIfRequired(employees);
    }

    public async Task<Employee?> GetEmployeeByIdAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesView);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return await _repository.GetByIdAsync(id);

        var employee = await context.Set<Employee>()
            .AsNoTracking()
            .Include(e => e.Party)
            .Include(e => e.JobRole)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (employee is not null && !_permissionService.HasPermission(PermissionKeys.EmployeesViewSalary))
        {
            RedactCompensation(employee);
        }
        return employee;
    }

    public async Task UpdateEmployeeAsync(Employee employee)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesEdit);
        _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        if (string.IsNullOrWhiteSpace(employee.Name))
            throw new System.ComponentModel.DataAnnotations.ValidationException("اسم الموظف مطلوب");

        if (string.IsNullOrWhiteSpace(employee.Code))
            throw new System.ComponentModel.DataAnnotations.ValidationException("كود الموظف مطلوب");

        if (employee.JobRoleId <= 0)
            throw new System.ComponentModel.DataAnnotations.ValidationException("يجب تحديد وظيفة / دور صالح للموظف");

        if (await _validationService.IsEmployeeCodeUsedAsync(employee.Code, employee.Id))
            throw new System.ComponentModel.DataAnnotations.ValidationException("كود الموظف مستخدم بالفعل");

        var party = await _partyRepository.GetByIdAsync(employee.PartyId);
        if (party != null)
        {
            party.Name = employee.Name;
            party.Phone = employee.Phone;
            party.Address = employee.Address;
            party.NationalId = employee.NationalId;
            await _partyRepository.UpdateAsync(party);
        }

        // Stamp wage metadata on every update
        employee.WageLastUpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(employee);
        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.Update, "Employee", employee.Id, null, employee.Name);
    }

    public async Task<EmployeeStats> GetEmployeeStatsAsync()
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesView);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return new EmployeeStats(0, 0, 0, 0);

        var activeEmployees = await context.Set<Employee>()
            .Include(e => e.JobRole)
            .Where(e => e.IsActive)
            .ToListAsync();
        
        var totalCount = await context.Set<Employee>().CountAsync();
        
        return new EmployeeStats(
            totalCount,
            activeEmployees.Count,
            // Use Employee's own MonthlySalary, not JobRole default
            _permissionService.HasPermission(PermissionKeys.EmployeesViewSalary)
                ? activeEmployees.Where(e => e.WageType == WageType.Monthly).Sum(e => e.MonthlySalary)
                : 0,
            activeEmployees.Count(e => e.WageType == WageType.Production)
        );
    }

    private IEnumerable<Employee> RedactCompensationIfRequired(List<Employee> employees)
    {
        if (_permissionService.HasPermission(PermissionKeys.EmployeesViewSalary))
        {
            return employees;
        }

        foreach (var employee in employees)
        {
            RedactCompensation(employee);
        }
        return employees;
    }

    private static void RedactCompensation(Employee employee)
    {
        employee.MonthlySalary = 0;
        employee.DailyRate = 0;
        employee.ProductionRate = 0;
        if (employee.JobRole is not null)
        {
            employee.JobRole.WageAmount = 0;
            employee.JobRole.MonthlySalary = 0;
            employee.JobRole.DailyRate = 0;
            employee.JobRole.ProductionRate = 0;
        }
    }
}
