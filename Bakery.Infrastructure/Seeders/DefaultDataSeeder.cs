using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Bakery.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bakery.Infrastructure.Seeders;

public sealed class DefaultDataSeeder
{
    private readonly BakeryDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISystemSafeService _systemSafeService;
    private readonly IInternalBranchContext? _branchContext;

    public DefaultDataSeeder(
        BakeryDbContext dbContext,
        IPasswordHasher passwordHasher,
        ISystemSafeService systemSafeService,
        IBranchContext? branchContext = null)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _systemSafeService = systemSafeService;
        _branchContext = branchContext?.AsInternal();
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // 1. Seed Default Branch if not exists
        var defaultBranch = await _dbContext.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Code == "MAIN", cancellationToken);
        if (defaultBranch is null)
        {
            defaultBranch = new Branch
            {
                Code = "MAIN",
                Name = "الفرع الرئيسي",
                IsActive = true
            };
            _dbContext.Branches.Add(defaultBranch);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // 2. Set Branch Context for the seeder
        _branchContext?.ConfigureBranch(new BranchDto(defaultBranch.Id, defaultBranch.Code, defaultBranch.Name, defaultBranch.IsActive, defaultBranch.Notes));

        try
        {
            var permissions = await SeedPermissionsAsync(cancellationToken);
            await MigratePermissionCompatibilityAsync(permissions, cancellationToken);
            var bootstrapUser = await SeedBootstrapUserAsync(permissions, cancellationToken);
            await SeedBuiltInRolesAsync(permissions, cancellationToken);
            await SeedSettingsAsync(cancellationToken);
            if (_systemSafeService is SystemSafeService systemSafeService)
            {
                await systemSafeService.EnsureSystemSafesCoreAsync(cancellationToken);
            }
            else
            {
                await _systemSafeService.EnsureSystemSafesAsync(cancellationToken);
            }

            if (bootstrapUser is not null)
            {
                var hasBranch = await _dbContext.UserBranches.AnyAsync(
                    ub => ub.UserId == bootstrapUser.Id && ub.BranchId == defaultBranch.Id,
                    cancellationToken);
                if (!hasBranch)
                {
                    _dbContext.UserBranches.Add(new UserBranch { UserId = bootstrapUser.Id, BranchId = defaultBranch.Id });
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // 3. Clear Branch Context
            _branchContext?.Clear();
        }
    }

    private async Task<List<Permission>> SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in PermissionCatalog.All)
        {
            var permission = await _dbContext.Permissions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Key == definition.Key, cancellationToken);

            if (permission is null)
            {
                _dbContext.Permissions.Add(new Permission
                {
                    Key = definition.Key,
                    DisplayName = definition.DisplayName,
                    Category = definition.Category
                });
            }
            else
            {
                permission.Key = definition.Key;
                permission.DisplayName = definition.DisplayName;
                permission.Category = definition.Category;
                permission.IsDeleted = false;
            }
        }
        // Soft-delete permissions that are no longer in the active catalog
        var activeKeys = PermissionCatalog.All.Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deprecatedPermissions = await _dbContext.Permissions
            .IgnoreQueryFilters()
            .Where(p => !activeKeys.Contains(p.Key) && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var deprecated in deprecatedPermissions)
        {
            deprecated.IsDeleted = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Compatibility Migration for User Management permissions
        var oldUserMgmtPermission = await _dbContext.Permissions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Key == "Settings.UserManagement", cancellationToken);

        if (oldUserMgmtPermission != null)
        {
            var usersWithOldPermission = await _dbContext.UserPermissions
                .IgnoreQueryFilters()
                .Where(up => up.PermissionId == oldUserMgmtPermission.Id)
                .Select(up => up.UserId)
                .ToListAsync(cancellationToken);

            if (usersWithOldPermission.Count > 0)
            {
                var newPermissions = await _dbContext.Permissions
                    .IgnoreQueryFilters()
                    .Where(p => p.Key == "Users.View" ||
                                p.Key == "Users.Add" ||
                                p.Key == "Users.Edit" ||
                                p.Key == "Users.Delete" ||
                                p.Key == "Users.ChangePermissions")
                    .ToListAsync(cancellationToken);

                foreach (var userId in usersWithOldPermission)
                {
                    foreach (var newPerm in newPermissions)
                    {
                        var exists = await _dbContext.UserPermissions
                            .IgnoreQueryFilters()
                            .AnyAsync(up => up.UserId == userId && up.PermissionId == newPerm.Id, cancellationToken);
                        if (!exists)
                        {
                            _dbContext.UserPermissions.Add(new UserPermission { UserId = userId, PermissionId = newPerm.Id });
                        }
                    }
                }
                
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var keys = PermissionCatalog.All.Select(permission => permission.Key).ToList();
        return await _dbContext.Permissions
            .Where(permission => keys.Contains(permission.Key))
            .ToListAsync(cancellationToken);
    }

    private async Task<User?> SeedBootstrapUserAsync(List<Permission> permissions, CancellationToken cancellationToken)
    {
        if (await _dbContext.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            // Never reactivate, undelete, rename, or elevate an existing account.
            return null;
        }

        var username = Environment.GetEnvironmentVariable("BAKERY_BOOTSTRAP_ADMIN_USERNAME")?.Trim();
        var password = Environment.GetEnvironmentVariable("BAKERY_BOOTSTRAP_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
        {
            // Interactive desktop installations create their first administrator in
            // the first-run setup window. Environment credentials remain supported
            // only for explicitly configured unattended deployments and tests.
            return null;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "إعداد مسؤول النظام غير مكتمل. يجب تعيين اسم المستخدم وكلمة المرور معاً، أو حذف المتغيرين لاستخدام شاشة الإعداد الأولي.");
        }

        PasswordPolicy.EnsureValid(password);

        var bootstrapUser = new User
        {
            Username = username,
            NormalizedUsername = NormalizeName(username),
            FullName = "مسؤول النظام",
            PasswordHash = _passwordHasher.HashPassword(password),
            IsActive = true,
            IsSuperAdmin = true,
            MustChangePassword = false,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        foreach (var permission in permissions)
        {
            bootstrapUser.UserPermissions.Add(new UserPermission
            {
                PermissionId = permission.Id
            });
        }

        _dbContext.Users.Add(bootstrapUser);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return bootstrapUser;
    }

    private async Task MigratePermissionCompatibilityAsync(
        IReadOnlyCollection<Permission> permissions,
        CancellationToken cancellationToken)
    {
        var byKey = permissions.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var compatibility = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PermissionKeys.SalesDelete] = [PermissionKeys.SalesCancel],
            [PermissionKeys.PurchasesDelete] = [PermissionKeys.PurchasesCancel],
            [PermissionKeys.PurchasesView] = [PermissionKeys.PurchasesPrint],
            [PermissionKeys.ProductionEdit] = [PermissionKeys.ProductionCancel],
            [PermissionKeys.ProductionView] = [PermissionKeys.ReportsProduction],
            [PermissionKeys.ProductsView] = [PermissionKeys.ProductsViewCost],
            [PermissionKeys.EmployeesSalaries] =
                [PermissionKeys.EmployeesViewSalary, PermissionKeys.EmployeesManagePayroll],
            [PermissionKeys.WorkingDayOpen] = [PermissionKeys.WorkingDayView],
            [PermissionKeys.WorkingDayClose] = [PermissionKeys.WorkingDayView],
            [PermissionKeys.WorkingDayReopen] = [PermissionKeys.WorkingDayView],
            [PermissionKeys.UsersEdit] = [PermissionKeys.UsersResetPassword],
            [PermissionKeys.UsersChangePermissions] =
                [PermissionKeys.RolesView, PermissionKeys.RolesAdd, PermissionKeys.RolesEdit,
                 PermissionKeys.RolesDelete, PermissionKeys.RolesAssign],
            [PermissionKeys.ReportsSales] = [PermissionKeys.ReportsPrint, PermissionKeys.ReportsExport],
            [PermissionKeys.ReportsInventory] = [PermissionKeys.ReportsPrint, PermissionKeys.ReportsExport],
            [PermissionKeys.ReportsFinancial] = [PermissionKeys.ReportsPrint, PermissionKeys.ReportsExport]
        };

        foreach (var (sourceKey, targetKeys) in compatibility)
        {
            if (!byKey.TryGetValue(sourceKey, out var source)) continue;
            var userIds = await _dbContext.UserPermissions
                .Where(item => item.PermissionId == source.Id)
                .Select(item => item.UserId)
                .ToListAsync(cancellationToken);
            foreach (var targetKey in targetKeys)
            {
                if (!byKey.TryGetValue(targetKey, out var target)) continue;
                var assignedUserIds = await _dbContext.UserPermissions
                    .Where(item => item.PermissionId == target.Id && userIds.Contains(item.UserId))
                    .Select(item => item.UserId)
                    .ToListAsync(cancellationToken);
                foreach (var userId in userIds.Except(assignedUserIds))
                {
                    if (!_dbContext.UserPermissions.Local.Any(item =>
                        item.UserId == userId && item.PermissionId == target.Id))
                    {
                        _dbContext.UserPermissions.Add(new UserPermission { UserId = userId, PermissionId = target.Id });
                    }
                }
            }
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedBuiltInRolesAsync(List<Permission> permissions, CancellationToken cancellationToken)
    {
        var allKeys = permissions.Select(permission => permission.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitions = new[]
        {
            new BuiltInRoleDefinition("مسؤول النظام", "صلاحيات النظام الكاملة", true, allKeys),
            new BuiltInRoleDefinition("مدير الفرع", "إدارة التشغيل اليومي للفرع", true, allKeys.Where(key =>
                !key.StartsWith("Users.", StringComparison.OrdinalIgnoreCase) &&
                !key.StartsWith("Roles.", StringComparison.OrdinalIgnoreCase) &&
                key != PermissionKeys.WorkingDayOverrideCloseBlockers &&
                key != PermissionKeys.SettingsResetSystem).ToHashSet(StringComparer.OrdinalIgnoreCase)),
            new BuiltInRoleDefinition("أمين خزينة", "المبيعات والتحصيل وعمليات الخزينة اليومية", false,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PermissionKeys.SalesView, PermissionKeys.SalesCreate, PermissionKeys.SalesPrint,
                    PermissionKeys.CustomersView, PermissionKeys.TreasuryView,
                    PermissionKeys.TreasuryCashIn, PermissionKeys.TreasuryCashOut,
                    PermissionKeys.CashDeposit, PermissionKeys.CashWithdraw,
                    PermissionKeys.WorkingDayView
                }),
            new BuiltInRoleDefinition("مراقب مخزون", "إدارة الأصناف والجرد والتسويات", false,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PermissionKeys.ProductsView, PermissionKeys.ProductsAdd, PermissionKeys.ProductsEdit,
                    PermissionKeys.InventoryView, PermissionKeys.InventoryStockAdjustments,
                    PermissionKeys.InventoryCount, PermissionKeys.ReportsInventory
                }),
            new BuiltInRoleDefinition("مدقق", "وصول رقابي للقراءة والتقارير وسجل التدقيق", false,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PermissionKeys.ReportsSales, PermissionKeys.ReportsInventory,
                    PermissionKeys.ReportsFinancial, PermissionKeys.ReportsProduction,
                    PermissionKeys.AuditView, PermissionKeys.WorkingDayView
                })
        };

        foreach (var definition in definitions)
        {
            var normalizedName = NormalizeName(definition.Name);
            var role = await _dbContext.Roles
                .IgnoreQueryFilters()
                .Include(item => item.RolePermissions)
                .SingleOrDefaultAsync(item => item.NormalizedName == normalizedName, cancellationToken);

            if (role is null)
            {
                role = new Role
                {
                    Name = definition.Name,
                    NormalizedName = normalizedName,
                    Description = definition.Description,
                    IsSystem = true,
                    IsProtected = definition.IsProtected
                };
                _dbContext.Roles.Add(role);
            }
            else
            {
                role.IsDeleted = false;
                role.IsSystem = true;
                role.IsProtected = definition.IsProtected;
                role.Description = definition.Description;
            }

            var permissionIds = permissions
                .Where(permission => definition.PermissionKeys.Contains(permission.Key))
                .Select(permission => permission.Id)
                .ToHashSet();
            var existingIds = role.RolePermissions.Select(item => item.PermissionId).ToHashSet();
            foreach (var permissionId in permissionIds.Where(existingIds.Add))
            {
                role.RolePermissions.Add(new RolePermission { PermissionId = permissionId });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var administratorRole = await _dbContext.Roles.SingleAsync(
            role => role.NormalizedName == NormalizeName("مسؤول النظام"),
            cancellationToken);
        var superAdminIds = await _dbContext.Users.IgnoreQueryFilters()
            .Where(user => user.IsSuperAdmin && !user.IsDeleted)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        var assignedIds = await _dbContext.UserRoles
            .Where(item => item.RoleId == administratorRole.Id && superAdminIds.Contains(item.UserId))
            .Select(item => item.UserId)
            .ToListAsync(cancellationToken);
        foreach (var userId in superAdminIds.Except(assignedIds))
        {
            _dbContext.UserRoles.Add(new UserRole { UserId = userId, RoleId = administratorRole.Id });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeName(string value) => value.Trim().ToUpperInvariant();

    private sealed record BuiltInRoleDefinition(
        string Name,
        string Description,
        bool IsProtected,
        HashSet<string> PermissionKeys);

    private async Task SeedSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = new[]
        {
            ("UiCulture", "ar-EG", "Default UI culture"),
            ("Inventory.AllowNegativeStock", "false", "Prevent negative stock sales"),
            ("Treasury.AllowNegativeSafeBalance", "false", "Prevent negative safe balances")
        };

        foreach (var (key, val, desc) in settings)
        {
            var setting = await _dbContext.AppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

            if (setting is null)
            {
                _dbContext.AppSettings.Add(new AppSetting { Key = key, Value = val, Description = desc });
            }
            else
            {
                setting.IsDeleted = false;
            }
        }
    }
}
