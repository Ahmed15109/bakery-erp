using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class EmployeeWageService : IEmployeeWageService
{
    private readonly IRepository<EmployeeWage> _repository;
    private readonly IRepository<PartyLedgerEntry> _partyLedgerEntryRepository;
    private readonly ISafeService _safeService;
    private readonly IWorkingDayService _workingDayService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly IPermissionService _permissionService;

    public EmployeeWageService(
        IRepository<EmployeeWage> repository, 
        IRepository<PartyLedgerEntry> partyLedgerEntryRepository,
        ISafeService safeService,
        IWorkingDayService workingDayService,
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        IPermissionService permissionService)
    {
        _repository = repository;
        _partyLedgerEntryRepository = partyLedgerEntryRepository;
        _safeService = safeService;
        _workingDayService = workingDayService;
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _permissionService = permissionService;
    }

    public async Task<EmployeeWage> CreateWageAsync(EmployeeWage wage)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        var added = await _repository.AddAsync(wage);
        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.Create, "EmployeeWage", added.Id, null, wage.Amount.ToString());
        return added;
    }

    public async Task DeleteWageAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        var wage = await _repository.GetByIdAsync(id);
        if (wage != null)
        {
            await _repository.DeleteAsync(wage);
            await _unitOfWork.SaveChangesAsync();
            await _auditService.LogAsync(AuditActionKeys.Delete, "EmployeeWage", id, null, wage.Amount.ToString());
        }
    }

    public async Task<IEnumerable<EmployeeWage>> GetAllWagesAsync()
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return await _repository.ListAsync();

        return await context.Set<EmployeeWage>()
            .Include(w => w.Employee)
            .Include(w => w.WorkingDay)
            .Include(w => w.Safe)
            .ToListAsync();
    }

    public async Task<EmployeeWage?> GetWageByIdAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return await _repository.GetByIdAsync(id);

        return await context.Set<EmployeeWage>()
            .Include(w => w.Employee)
            .Include(w => w.WorkingDay)
            .Include(w => w.Safe)
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task UpdateWageAsync(EmployeeWage wage)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesManagePayroll);
        await _repository.UpdateAsync(wage);
        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.Update, "EmployeeWage", wage.Id, null, wage.Amount.ToString());
    }
}
