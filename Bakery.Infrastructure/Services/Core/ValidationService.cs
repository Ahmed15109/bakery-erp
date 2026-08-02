using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class ValidationService : IValidationService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IPermissionService _permissionService;

    public ValidationService(BakeryDbContext dbContext, IPermissionService permissionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
    }

    public async Task<bool> IsItemCodeUsedAsync(string code, int? excludeId = null)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsView);
        if (string.IsNullOrWhiteSpace(code)) return false;
        var q = _dbContext.Items.Where(x => x.Code == code.Trim() && !x.IsDeleted);
        if (excludeId.HasValue) q = q.Where(x => x.Id != excludeId.Value);
        return await q.AnyAsync();
    }

    public async Task<bool> IsBarcodeUsedAsync(string? barcode, int? excludeId = null)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsView);
        if (string.IsNullOrWhiteSpace(barcode)) return false;
        var q = _dbContext.Items.Where(x => x.Barcode == barcode.Trim() && !x.IsDeleted);
        if (excludeId.HasValue) q = q.Where(x => x.Id != excludeId.Value);
        return await q.AnyAsync();
    }

    public async Task<bool> IsUsernameUsedAsync(string username, int? excludeId = null)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersView);
        if (string.IsNullOrWhiteSpace(username)) return false;
        var trimmedUsername = username.Trim();
        var normalizedUsername = trimmedUsername.ToUpperInvariant();
        var q = _dbContext.Users.IgnoreQueryFilters().Where(x =>
            x.NormalizedUsername == normalizedUsername || x.Username == trimmedUsername);
        if (excludeId.HasValue) q = q.Where(x => x.Id != excludeId.Value);
        return await q.AnyAsync();
    }

    public async Task<bool> IsEmployeeCodeUsedAsync(string code, int? excludeId = null)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesView);
        if (string.IsNullOrWhiteSpace(code)) return false;
        var q = _dbContext.Employees.Where(x => x.Code == code.Trim() && !x.IsDeleted);
        if (excludeId.HasValue) q = q.Where(x => x.Id != excludeId.Value);
        return await q.AnyAsync();
    }

    public async Task<bool> IsSafeNameUsedAsync(string name, int? excludeId = null)
    {
        _permissionService.EnsurePermission(PermissionKeys.TreasuryManageSafes);
        if (string.IsNullOrWhiteSpace(name)) return false;
        var q = _dbContext.Safes.Where(x => x.Name == name.Trim() && !x.IsDeleted);
        if (excludeId.HasValue) q = q.Where(x => x.Id != excludeId.Value);
        return await q.AnyAsync();
    }

    public async Task<bool> IsJobRoleNameUsedAsync(string name, int? excludeId = null)
    {
        _permissionService.EnsurePermission(PermissionKeys.EmployeesView);
        if (string.IsNullOrWhiteSpace(name)) return false;
        var q = _dbContext.JobRoles.Where(x => x.Name == name.Trim() && !x.IsDeleted);
        if (excludeId.HasValue) q = q.Where(x => x.Id != excludeId.Value);
        return await q.AnyAsync();
    }

    public async Task<bool> IsPartyNameUsedAsync(string name, int? excludeId = null)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.CustomersView,
            PermissionKeys.PurchasesView,
            PermissionKeys.EmployeesView);
        if (string.IsNullOrWhiteSpace(name)) return false;
        var q = _dbContext.Parties.Where(x => x.Name == name.Trim() && !x.IsDeleted);
        if (excludeId.HasValue) q = q.Where(x => x.Id != excludeId.Value);
        return await q.AnyAsync();
    }
}
