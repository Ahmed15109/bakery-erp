using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bakery.Infrastructure.Services;

public sealed class BranchService : IBranchService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IPermissionService _permissionService;
    private readonly IBranchContext _branchContext;
    private readonly IBranchProvisioningService _branchProvisioningService;
    private readonly IAuditService _auditService;
    private readonly IValidator<CreateBranchRequest> _createValidator;
    private readonly IValidator<UpdateBranchRequest> _updateValidator;

    public BranchService(
        BakeryDbContext dbContext,
        IPermissionService permissionService,
        IBranchContext branchContext,
        IBranchProvisioningService branchProvisioningService,
        IAuditService auditService,
        IValidator<CreateBranchRequest> createValidator,
        IValidator<UpdateBranchRequest> updateValidator)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
        _branchContext = branchContext;
        _branchProvisioningService = branchProvisioningService;
        _auditService = auditService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    private void EnsureBranchManagementPermission()
    {
        _permissionService.EnsurePermission(PermissionKeys.SettingsBranchManagement);
    }

    public async Task<IReadOnlyList<BranchDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.SettingsBranchManagement,
            PermissionKeys.UsersAdd,
            PermissionKeys.UsersEdit);

        return await _dbContext.Branches
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new BranchDto(b.Id, b.Code, b.Name, b.IsActive, b.Notes))
            .ToListAsync(cancellationToken);
    }

    public async Task<BranchDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureBranchManagementPermission();

        var branch = await _dbContext.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        return branch is null ? null : new BranchDto(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.Notes);
    }

    public async Task<BranchDto> CreateAsync(CreateBranchRequest request, CancellationToken cancellationToken = default)
    {
        EnsureBranchManagementPermission();
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _dbContext.Branches.IgnoreQueryFilters().AnyAsync(b => b.Code == code, cancellationToken))
        {
            throw new ValidationException("كود الفرع موجود بالفعل.");
        }

        var name = request.Name.Trim();
        if (await _dbContext.Branches.IgnoreQueryFilters().AnyAsync(b => b.Name == name, cancellationToken))
        {
            throw new ValidationException("اسم الفرع موجود بالفعل.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var branch = new Branch
            {
                Code = code,
                Name = name,
                IsActive = true,
                Notes = request.Notes?.Trim()
            };

            _dbContext.Branches.Add(branch);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Provision system safes & settings for the new branch
            await _branchProvisioningService.ProvisionBranchAsync(branch.Id, cancellationToken);

            await _auditService.LogAsync(AuditActionKeys.BranchCreated, "Branch", branch.Id, null, branch.Name, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new BranchDto(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.Notes);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<BranchDto> UpdateAsync(UpdateBranchRequest request, CancellationToken cancellationToken = default)
    {
        EnsureBranchManagementPermission();
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var branch = await _dbContext.Branches.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("الفرع غير موجود.");

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _dbContext.Branches.IgnoreQueryFilters().AnyAsync(b => b.Id != branch.Id && b.Code == code, cancellationToken))
        {
            throw new ValidationException("كود الفرع موجود بالفعل.");
        }

        var name = request.Name.Trim();
        if (await _dbContext.Branches.IgnoreQueryFilters().AnyAsync(b => b.Id != branch.Id && b.Name == name, cancellationToken))
        {
            throw new ValidationException("اسم الفرع موجود بالفعل.");
        }

        // Prevent disabling default MAIN branch
        if (branch.Code == "MAIN" && !request.IsActive)
        {
            throw new ValidationException("لا يمكن إلغاء تنشيط الفرع الرئيسي.");
        }

        // Prevent disabling the current branch user is working on
        if (_branchContext.CurrentBranchId == branch.Id && !request.IsActive)
        {
            throw new ValidationException("لا يمكن إلغاء تنشيط الفرع الذي تعمل عليه حالياً.");
        }

        branch.Code = code;
        branch.Name = name;
        branch.IsActive = request.IsActive;
        branch.Notes = request.Notes?.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(AuditActionKeys.BranchUpdated, "Branch", branch.Id, null, branch.Name, cancellationToken);

        return new BranchDto(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.Notes);
    }

    public async Task SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        EnsureBranchManagementPermission();

        var branch = await _dbContext.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("الفرع غير موجود.");

        if (branch.Code == "MAIN" && !isActive)
        {
            throw new ValidationException("لا يمكن إلغاء تنشيط الفرع الرئيسي.");
        }

        if (_branchContext.CurrentBranchId == branch.Id && !isActive)
        {
            throw new ValidationException("لا يمكن إلغاء تنشيط الفرع الذي تعمل عليه حالياً.");
        }

        branch.IsActive = isActive;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(isActive ? AuditActionKeys.BranchActivated : AuditActionKeys.BranchDeactivated, "Branch", branch.Id, null, branch.Name, cancellationToken);
    }

    public async Task<bool> CanDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureBranchManagementPermission();
        var branch = await _dbContext.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null || branch.IsDeleted || branch.Code == "MAIN" || _branchContext.CurrentBranchId == id)
        {
            return false;
        }

        // Prevent physical or soft deleting if the branch has any master or transactional data
        // Check dependent tables (checking non-deleted entities)
        var hasItems = await _dbContext.Items.IgnoreQueryFilters().AnyAsync(x => x.BranchId == id && !x.IsDeleted, cancellationToken);
        if (hasItems) return false;

        var hasParties = await _dbContext.Parties.IgnoreQueryFilters().AnyAsync(x => x.BranchId == id && !x.IsDeleted, cancellationToken);
        if (hasParties) return false;

        var hasEmployees = await _dbContext.Employees.IgnoreQueryFilters().AnyAsync(x => x.BranchId == id && !x.IsDeleted, cancellationToken);
        if (hasEmployees) return false;

        var hasWorkingDays = await _dbContext.WorkingDays.IgnoreQueryFilters().AnyAsync(x => x.BranchId == id && !x.IsDeleted, cancellationToken);
        if (hasWorkingDays) return false;

        var hasSaleInvoices = await _dbContext.SaleInvoices.IgnoreQueryFilters().AnyAsync(x => x.BranchId == id && !x.IsDeleted, cancellationToken);
        if (hasSaleInvoices) return false;

        var hasPurchaseInvoices = await _dbContext.PurchaseInvoices.IgnoreQueryFilters().AnyAsync(x => x.BranchId == id && !x.IsDeleted, cancellationToken);
        if (hasPurchaseInvoices) return false;

        var hasProductionOrders = await _dbContext.ProductionOrders.IgnoreQueryFilters().AnyAsync(x => x.BranchId == id && !x.IsDeleted, cancellationToken);
        if (hasProductionOrders) return false;

        var hasWasteEntries = await _dbContext.WasteEntries.IgnoreQueryFilters().AnyAsync(x => x.BranchId == id && !x.IsDeleted, cancellationToken);
        if (hasWasteEntries) return false;

        var hasExpenses = await _dbContext.Expenses.IgnoreQueryFilters().AnyAsync(x => x.BranchId == id && !x.IsDeleted, cancellationToken);
        if (hasExpenses) return false;

        var hasSafes = await _dbContext.Safes.IgnoreQueryFilters().AnyAsync(x => x.BranchId == id && !x.IsDeleted && x.Type == SafeType.Normal, cancellationToken);
        if (hasSafes) return false;

        return true;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureBranchManagementPermission();

        var branch = await _dbContext.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("الفرع غير موجود.");

        if (!await CanDeleteAsync(id, cancellationToken))
        {
            throw new ValidationException("لا يمكن حذف هذا الفرع لوجود بيانات مرتبطة به. يرجى إلغاء تنشيطه بدلاً من ذلك.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Delete associated system safes and permissions before soft deleting
            var safes = await _dbContext.Safes.IgnoreQueryFilters().Where(x => x.BranchId == id).ToListAsync(cancellationToken);
            _dbContext.Safes.RemoveRange(safes);

            var userBranches = await _dbContext.UserBranches.Where(x => x.BranchId == id).ToListAsync(cancellationToken);
            _dbContext.UserBranches.RemoveRange(userBranches);

            var settings = await _dbContext.AppSettings.IgnoreQueryFilters().Where(x => x.BranchId == id).ToListAsync(cancellationToken);
            _dbContext.AppSettings.RemoveRange(settings);

            _dbContext.Branches.Remove(branch);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(AuditActionKeys.BranchDeleted, "Branch", branch.Id, null, branch.Name, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<BranchDto>> GetUserBranchesAsync(int userId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.BranchesSwitch, PermissionKeys.UsersView);
        return await _dbContext.UserBranches
            .AsNoTracking()
            .Where(ub => ub.UserId == userId && ub.Branch.IsActive && !ub.Branch.IsDeleted)
            .Select(ub => new BranchDto(ub.Branch.Id, ub.Branch.Code, ub.Branch.Name, ub.Branch.IsActive, ub.Branch.Notes))
            .ToListAsync(cancellationToken);
    }
}
