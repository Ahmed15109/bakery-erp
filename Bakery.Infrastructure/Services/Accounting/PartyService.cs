using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class PartyService : IPartyService
{
    private readonly BakeryDbContext _db;
    private readonly IValidator<SavePartyRequest> _validator;
    private readonly IValidationService _validationService;
    private readonly IPermissionService _permissionService;
    private readonly IBranchContext _branchContext;

    public PartyService(BakeryDbContext db, IValidator<SavePartyRequest> validator, IValidationService validationService, IPermissionService permissionService, IBranchContext branchContext)
    { 
        _db = db; 
        _validator = validator; 
        _validationService = validationService;
        _permissionService = permissionService;
        _branchContext = branchContext;
    }

    public async Task<IReadOnlyList<PartyDto>> SearchAsync(PartySearchRequest request, CancellationToken ct = default)
    {
        var canViewCustomers = _permissionService.HasPermission(PermissionKeys.CustomersView);
        var canViewSuppliers = _permissionService.HasPermission(PermissionKeys.PurchasesView);
        var canViewEmployees = _permissionService.HasPermission(PermissionKeys.EmployeesView);

        return await ExecuteSearchInternalAsync(canViewCustomers, canViewSuppliers, canViewEmployees, request, ct);
    }

    public async Task<IReadOnlyList<PartyDto>> LookupAsync(PartySearchRequest request, CancellationToken ct = default)
    {
        var canViewCustomers = _permissionService.HasPermission(PermissionKeys.CustomersView) || _permissionService.HasPermission(PermissionKeys.SalesCreate);
        var canViewSuppliers = _permissionService.HasPermission(PermissionKeys.PurchasesView) || _permissionService.HasPermission(PermissionKeys.PurchasesCreate);
        var canViewEmployees = _permissionService.HasPermission(PermissionKeys.EmployeesView);

        return await ExecuteSearchInternalAsync(canViewCustomers, canViewSuppliers, canViewEmployees, request, ct);
    }

    private async Task<IReadOnlyList<PartyDto>> ExecuteSearchInternalAsync(
        bool canViewCustomers,
        bool canViewSuppliers,
        bool canViewEmployees,
        PartySearchRequest request,
        CancellationToken ct)
    {
        if (!canViewCustomers && !canViewSuppliers && !canViewEmployees)
        {
            return [];
        }

        var query = _db.Parties.AsQueryable();
        if (request.IncludeDeleted)
        {
            var currentBranchId = _branchContext.CurrentBranchId ?? 0;
            query = query.IgnoreQueryFilters().Where(x => x.BranchId == currentBranchId);
        }
        
        if (!string.IsNullOrWhiteSpace(request.Search)) query = query.Where(x => x.Name.Contains(request.Search) || (x.Phone != null && x.Phone.Contains(request.Search)));
        
        if (!request.Type.HasValue)
        {
            var allowedTypes = new List<PartyType>();
            if (canViewCustomers) { allowedTypes.Add(PartyType.Customer); allowedTypes.Add(PartyType.Mixed); }
            if (canViewSuppliers) { allowedTypes.Add(PartyType.Supplier); if (!allowedTypes.Contains(PartyType.Mixed)) allowedTypes.Add(PartyType.Mixed); }
            if (canViewEmployees) { allowedTypes.Add(PartyType.Employee); }
            query = query.Where(x => allowedTypes.Contains(x.Type));
        }
        else
        {
            if (request.Type.Value == PartyType.Customer)
            {
                if (!canViewCustomers) return [];
                query = query.Where(x => x.Type == PartyType.Customer || x.Type == PartyType.Mixed);
            }
            else if (request.Type.Value == PartyType.Supplier)
            {
                if (!canViewSuppliers) return [];
                query = query.Where(x => x.Type == PartyType.Supplier || x.Type == PartyType.Mixed);
            }
            else
            {
                if (request.Type.Value == PartyType.Employee && !canViewEmployees) return [];
                query = query.Where(x => x.Type == request.Type.Value);
            }
        }
        if (request.IsActive.HasValue) query = query.Where(x => x.IsActive == request.IsActive.Value);
        
        query = query.OrderBy(x => x.Name);
        if (request.Limit.HasValue) query = query.Take(request.Limit.Value);
        
        var parties = await query.AsNoTracking().ToListAsync(ct);
        var balances = await _db.PartyLedgerEntries.GroupBy(x => x.PartyId).Select(g => new { PartyId = g.Key, Balance = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.PartyId, x => x.Balance, ct);
        return parties.Select(p => new PartyDto(p.Id, p.Name, p.Type, p.Phone, p.Address, p.NationalId, p.Notes, p.IsActive, balances.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<DuplicateValidationResult> CheckNameDuplicatesAsync(string name, int? excludeId = null, CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.CustomersView, PermissionKeys.PurchasesView, PermissionKeys.EmployeesView);
        if (string.IsNullOrWhiteSpace(name))
            return new DuplicateValidationResult(false, [], string.Empty);

        var query = _db.Parties.Where(x => x.Name == name.Trim());
        if (excludeId.HasValue)
            query = query.Where(x => x.Id != excludeId.Value);

        var matches = await query.AsNoTracking().ToListAsync(ct);
        
        if (matches.Count == 0)
            return new DuplicateValidationResult(false, [], string.Empty);

        var msgBuilder = new System.Text.StringBuilder();
        msgBuilder.AppendLine("توجد أطراف مسجلة بنفس الاسم:\n");
        foreach (var p in matches)
        {
            var typeAr = p.Type switch
            {
                PartyType.Customer => "عميل",
                PartyType.Supplier => "مورد",
                PartyType.Employee => "موظف",
                _ => p.Type.ToString()
            };
            msgBuilder.AppendLine($"• {p.Name}");
            msgBuilder.AppendLine($"  النوع: {typeAr}");
            if (!string.IsNullOrWhiteSpace(p.Phone)) msgBuilder.AppendLine($"  رقم الهاتف: {p.Phone}");
            if (!string.IsNullOrWhiteSpace(p.NationalId)) msgBuilder.AppendLine($"  الرقم القومي: {p.NationalId}");
            msgBuilder.AppendLine();
        }
        msgBuilder.AppendLine("هل تريد الاستمرار وحفظ السجل الجديد بنفس الاسم؟");

        var dtos = matches.Select(p => new PartyDto(p.Id, p.Name, p.Type, p.Phone, p.Address, p.NationalId, p.Notes, p.IsActive, 0)).ToList();
        return new DuplicateValidationResult(true, dtos, msgBuilder.ToString());
    }

    public async Task<(bool Succeeded, string? ErrorMessage, PartyDto? Party)> SaveAsync(SavePartyRequest request, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(GetSavePermission(request.Type, request.Id is null or 0));
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return (false, validation.Errors[0].ErrorMessage, null);

        // Note: Soft validation is now handled in the UI calling CheckNameDuplicatesAsync
        // We no longer block saving at the service layer for duplicate names.

        var party = request.Id is null or 0 ? new Party() : await _db.Parties.FirstAsync(x => x.Id == request.Id, ct);
        if (request.Id is null or 0) _db.Parties.Add(party);
        party.Name = request.Name.Trim(); party.Type = request.Type; party.Phone = request.Phone; party.Address = request.Address; party.NationalId = request.NationalId; party.Notes = request.Notes; party.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        
        var dto = (await SearchAsync(new PartySearchRequest { Search = party.Name, Limit = 1 }, ct)).First(x => x.Id == party.Id);
        return (true, null, dto);
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> SetActiveAsync(int id, bool isActive, CancellationToken ct = default)
    {
        var party = await _db.Parties.FirstAsync(x => x.Id == id, ct);
        _permissionService.EnsurePermission(GetEditPermission(party.Type));
        party.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken ct = default)
    {
        var partyForPermission = await _db.Parties.FindAsync(new object[] { id }, ct);
        if (partyForPermission == null) return (false, "Party was not found.");
        _permissionService.EnsurePermission(GetDeletePermission(partyForPermission.Type));

        var hasEntries = await _db.PartyLedgerEntries.AnyAsync(x => x.PartyId == id, ct);
        if (hasEntries) return (false, "لا يمكن حذف الطرف لوجود حركات مالية مسجلة. يمكنك إلغاء تنشيطه بدلاً من ذلك.");

        var party = await _db.Parties.FindAsync(new object[] { id }, ct);
        if (party == null) return (false, "الطرف غير موجود.");

        // Check for associated employee and handle its deletion first to prevent orphaning / ChangeTracker exceptions
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.PartyId == id, ct);
        if (employee != null)
        {
            var hasWages = await _db.EmployeeWages.AnyAsync(w => w.EmployeeId == employee.Id, ct);
            var hasProduction = await _db.ProductionOrderEmployees.AnyAsync(p => p.EmployeeId == employee.Id, ct);
            var hasAttendance = await _db.Attendances.AnyAsync(a => a.EmployeeId == employee.Id, ct);
            if (hasWages || hasProduction || hasAttendance)
            {
                return (false, "لا يمكن حذف الطرف لوجود حركات مسجلة باسمه كموظف (أجور، إنتاج، أو حضور).");
            }

            employee.IsDeleted = true;
            _db.Employees.Update(employee);
        }

        // Soft-delete the party directly by setting IsDeleted = true
        party.IsDeleted = true;
        party.IsActive = false;
        _db.Parties.Update(party);

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    private static string GetSavePermission(PartyType type, bool isNew)
    {
        return type switch
        {
            PartyType.Customer or PartyType.Mixed => isNew ? PermissionKeys.CustomersAdd : PermissionKeys.CustomersEdit,
            PartyType.Supplier => isNew ? PermissionKeys.PurchasesCreate : PermissionKeys.PurchasesEdit,
            PartyType.Employee => isNew ? PermissionKeys.EmployeesAdd : PermissionKeys.EmployeesEdit,
            _ => PermissionKeys.AccountingView
        };
    }

    private static string GetEditPermission(PartyType type)
    {
        return type switch
        {
            PartyType.Customer or PartyType.Mixed => PermissionKeys.CustomersEdit,
            PartyType.Supplier => PermissionKeys.PurchasesEdit,
            PartyType.Employee => PermissionKeys.EmployeesEdit,
            _ => PermissionKeys.AccountingView
        };
    }

    private static string GetDeletePermission(PartyType type)
    {
        return type switch
        {
            PartyType.Customer or PartyType.Mixed => PermissionKeys.CustomersDelete,
            PartyType.Supplier => PermissionKeys.PurchasesDelete,
            PartyType.Employee => PermissionKeys.EmployeesDelete,
            _ => PermissionKeys.AccountingView
        };
    }

    public async Task<decimal> GetBalanceAsync(int partyId, CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.CustomersView, PermissionKeys.PurchasesView,
            PermissionKeys.EmployeesViewSalary, PermissionKeys.SalesCreate,
            PermissionKeys.PurchasesCreate, PermissionKeys.TreasuryCashIn,
            PermissionKeys.TreasuryCashOut, PermissionKeys.AccountingView,
            PermissionKeys.AccountingCustomerLedger, PermissionKeys.AccountingSupplierLedger);
        var partyType = await _db.Parties.AsNoTracking()
            .Where(party => party.Id == partyId)
            .Select(party => (PartyType?)party.Type)
            .SingleOrDefaultAsync(ct);
        if (partyType.HasValue) EnsureBalanceAccess(partyType.Value);
        return await _db.PartyLedgerEntries.Where(x => x.PartyId == partyId).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
    }

    public async Task<PartySummaryDto> GetPartySummaryAsync(int partyId, CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.CustomersView, PermissionKeys.PurchasesView,
            PermissionKeys.EmployeesViewSalary, PermissionKeys.AccountingView,
            PermissionKeys.AccountingCustomerLedger, PermissionKeys.AccountingSupplierLedger);
        var party = await _db.Parties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == partyId, ct);
        if (party == null) return new PartySummaryDto("N/A", PartyType.Customer, 0, 0, 0);
        EnsureSummaryAccess(party.Type);

        // Employees use EmployeeTransactions — not PartyLedgerEntries (which are always empty for employees)
        if (party.Type == PartyType.Employee)
        {
            var employee = await _db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.PartyId == partyId, ct);

            if (employee == null)
                return new PartySummaryDto(party.Name, PartyType.Employee, 0, 0, 0);

            var txList = await _db.Set<Domain.Entities.EmployeeTransaction>()
                .AsNoTracking()
                .Where(t => t.EmployeeId == employee.Id)
                .ToListAsync(ct);

            decimal earned = txList
                .Where(t => t.Type == Domain.Enums.EmployeeTransactionType.Earned ||
                            t.Type == Domain.Enums.EmployeeTransactionType.Bonus)
                .Sum(t => t.Amount);

            decimal paid = txList
                .Where(t => t.Type == Domain.Enums.EmployeeTransactionType.Advance ||
                            t.Type == Domain.Enums.EmployeeTransactionType.SalaryPayment ||
                            t.Type == Domain.Enums.EmployeeTransactionType.Deduction)
                .Sum(t => t.Amount);

            return new PartySummaryDto(party.Name, PartyType.Employee, earned, paid, earned - paid);
        }

        var allEntries = await _db.PartyLedgerEntries
            .Where(x => x.PartyId == partyId && !x.IsReversed && x.ReversalReferenceId == null)
            .ToListAsync(ct);

        decimal totalIncrease = 0; // Sales or Purchases
        decimal totalDecrease = 0; // Collected or Paid

        foreach (var entry in allEntries)
        {
            var impact = entry.GetAccountingImpact(party.Type);
            totalIncrease += impact.IncreaseAmount;
            totalDecrease += impact.DecreaseAmount;
        }

        // The remaining balance mathematically equals the net ledger amount
        decimal balance = totalIncrease - totalDecrease;

        return new PartySummaryDto(party.Name, party.Type, totalIncrease, totalDecrease, balance);
    }

    public async Task ValidateBalanceLimitAsync(int partyId, decimal reductionAmount, decimal invoiceTotal = 0, CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.SalesCreate, PermissionKeys.PurchasesCreate, PermissionKeys.TreasuryCashIn, PermissionKeys.TreasuryCashOut);
        var allowNegative = await _db.AppSettings.AnyAsync(s => s.Key == "Accounting.AllowNegativePartyBalance" && s.Value == "true", ct);
        if (allowNegative) return;

        var currentBalance = await GetBalanceAsync(partyId, ct);
        if (currentBalance + invoiceTotal < reductionAmount)
        {
            var party = await _db.Parties.FindAsync(new object[] { partyId }, ct);
            string message = party?.Type == PartyType.Customer 
                ? "لا يمكن تسجيل تحصيل أكبر من مديونية العميل."
                : "لا يمكن سداد مبلغ أكبر من مديونية المورد.";
            throw new ValidationException($"{message} (المديونية الحالية: {currentBalance:N2} ج.م)");
        }
    }

    public async Task<PartyStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.AccountingView);
        var customers = await _db.Parties.CountAsync(x => x.Type == PartyType.Customer, ct);
        var suppliers = await _db.Parties.CountAsync(x => x.Type == PartyType.Supplier, ct);
        var employees = await _db.Parties.CountAsync(x => x.Type == PartyType.Employee, ct);
        var totalBalance = await _db.PartyLedgerEntries.SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        return new PartyStatsDto(customers, suppliers, employees, totalBalance);
    }

    private void EnsureBalanceAccess(PartyType partyType)
    {
        switch (partyType)
        {
            case PartyType.Customer:
                _permissionService.EnsureAnyPermission(
                    PermissionKeys.CustomersView, PermissionKeys.SalesCreate,
                    PermissionKeys.TreasuryCashIn, PermissionKeys.AccountingCustomerLedger,
                    PermissionKeys.AccountingView);
                break;
            case PartyType.Supplier:
                _permissionService.EnsureAnyPermission(
                    PermissionKeys.PurchasesView, PermissionKeys.PurchasesCreate,
                    PermissionKeys.TreasuryCashOut, PermissionKeys.AccountingSupplierLedger,
                    PermissionKeys.AccountingView);
                break;
            case PartyType.Employee:
                _permissionService.EnsureAnyPermission(
                    PermissionKeys.EmployeesViewSalary, PermissionKeys.EmployeesManagePayroll,
                    PermissionKeys.EmployeesAdvances);
                break;
            case PartyType.Mixed:
                _permissionService.EnsureAnyPermission(
                    PermissionKeys.AccountingView, PermissionKeys.SalesCreate,
                    PermissionKeys.PurchasesCreate, PermissionKeys.TreasuryCashIn,
                    PermissionKeys.TreasuryCashOut);
                break;
        }
    }

    private void EnsureSummaryAccess(PartyType partyType)
    {
        switch (partyType)
        {
            case PartyType.Customer:
                _permissionService.EnsureAnyPermission(
                    PermissionKeys.CustomersView, PermissionKeys.AccountingCustomerLedger,
                    PermissionKeys.AccountingView);
                break;
            case PartyType.Supplier:
                _permissionService.EnsureAnyPermission(
                    PermissionKeys.PurchasesView, PermissionKeys.AccountingSupplierLedger,
                    PermissionKeys.AccountingView);
                break;
            case PartyType.Employee:
                _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
                break;
            case PartyType.Mixed:
                if (!_permissionService.HasPermission(PermissionKeys.AccountingView))
                {
                    _permissionService.EnsurePermission(PermissionKeys.CustomersView);
                    _permissionService.EnsurePermission(PermissionKeys.PurchasesView);
                }
                break;
        }
    }
}
