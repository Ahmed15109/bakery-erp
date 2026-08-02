using System.Data;
using System.Text.Json;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class RoleManagementService : IRoleManagementService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IPermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly IUserSessionService _userSessionService;

    public RoleManagementService(
        BakeryDbContext dbContext,
        IPermissionService permissionService,
        IAuditService auditService,
        IUserSessionService userSessionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
        _auditService = auditService;
        _userSessionService = userSessionService;
    }

    public async Task<IReadOnlyList<RoleListItemDto>> SearchAsync(
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.RolesView);
        var query = _dbContext.Roles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.Trim();
            query = query.Where(role => role.Name.Contains(search) ||
                (role.Description != null && role.Description.Contains(search)));
        }

        return await query.OrderByDescending(role => role.IsProtected).ThenBy(role => role.Name)
            .Select(role => new RoleListItemDto(
                role.Id,
                role.Name,
                role.Description,
                role.IsSystem,
                role.IsProtected,
                role.UserRoles.Count,
                role.RolePermissions.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<RoleDetailsDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.RolesView);
        var role = await GetRoleGraphAsync(id, cancellationToken);
        return role is null ? null : ToDto(role);
    }

    public async Task<RoleDetailsDto> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.RolesAdd);
        _permissionService.EnsurePermission(PermissionKeys.UsersChangePermissions);
        ValidateRequest(request);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var normalizedName = Normalize(request.Name);
            if (await _dbContext.Roles.IgnoreQueryFilters().AnyAsync(
                role => role.NormalizedName == normalizedName,
                cancellationToken))
            {
                throw new InvalidOperationException("اسم الدور مستخدم بالفعل أو مرتبط بدور سابق.");
            }

            var permissions = await LoadPermissionsAsync(request.PermissionKeys, cancellationToken);
            var role = new Role
            {
                Name = request.Name.Trim(),
                NormalizedName = normalizedName,
                Description = NullIfWhiteSpace(request.Description)
            };
            foreach (var permission in permissions)
                role.RolePermissions.Add(new RolePermission { Permission = permission });

            _dbContext.Roles.Add(role);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(AuditActionKeys.RoleCreated, nameof(Role), role.Id, null, Serialize(role), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDto(role);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<RoleDetailsDto> UpdateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.RolesEdit);
        _permissionService.EnsurePermission(PermissionKeys.UsersChangePermissions);
        ValidateRequest(request);
        if (request.Id is null)
            throw new InvalidOperationException("معرف الدور مطلوب.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var role = await GetRoleGraphAsync(request.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("لم يتم العثور على الدور.");
            ApplyExpectedRowVersion(role, request.RowVersion);
            if (role.IsProtected && !_permissionService.IsAdmin())
            {
                throw new UnauthorizedAccessException("لا يمكن تعديل الدور المحمي إلا بواسطة مسؤول النظام.");
            }

            var normalizedName = Normalize(request.Name);
            if (role.IsProtected && role.NormalizedName != normalizedName)
                throw new InvalidOperationException("لا يمكن تغيير اسم الدور المحمي.");
            if (await _dbContext.Roles.IgnoreQueryFilters().AnyAsync(
                item => item.Id != role.Id && item.NormalizedName == normalizedName,
                cancellationToken))
                throw new InvalidOperationException("اسم الدور مستخدم بالفعل أو مرتبط بدور سابق.");

            var permissions = await LoadPermissionsAsync(request.PermissionKeys, cancellationToken);
            if (role.IsProtected && role.RolePermissions.Any(item => item.Permission.Key == PermissionKeys.UsersChangePermissions) &&
                permissions.All(permission => permission.Key != PermissionKeys.UsersChangePermissions))
                throw new InvalidOperationException("لا يمكن إزالة صلاحية إدارة الصلاحيات من دور مسؤول النظام المحمي.");

            var removesAdministratorPermission = role.RolePermissions.Any(item =>
                    item.Permission.Key == PermissionKeys.UsersChangePermissions) &&
                permissions.All(permission => permission.Key != PermissionKeys.UsersChangePermissions);
            if (removesAdministratorPermission)
            {
                await EnsureAdministratorContinuityAsync(role.Id, cancellationToken);
            }

            var oldState = Serialize(role);
            role.Name = request.Name.Trim();
            role.NormalizedName = normalizedName;
            role.Description = NullIfWhiteSpace(request.Description);
            role.RolePermissions.Clear();
            foreach (var permission in permissions)
                role.RolePermissions.Add(new RolePermission { Permission = permission });

            var affectedUsers = await _dbContext.Users
                .Where(user => user.UserRoles.Any(item => item.RoleId == role.Id))
                .ToListAsync(cancellationToken);
            foreach (var user in affectedUsers)
                user.SecurityStamp = Guid.NewGuid().ToString("N");

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(AuditActionKeys.RoleUpdated, nameof(Role), role.Id, oldState, Serialize(role), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (_userSessionService.UserId is int currentUserId && affectedUsers.Any(user => user.Id == currentUserId))
            {
                _userSessionService.InvalidateIfCurrentUser(currentUserId);
            }
            return ToDto(role);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("تم تعديل الدور بواسطة جلسة أخرى. حدّث البيانات ثم أعد المحاولة.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.RolesDelete);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var role = await GetRoleGraphAsync(id, cancellationToken)
                ?? throw new InvalidOperationException("لم يتم العثور على الدور.");
            if (role.IsProtected)
                throw new InvalidOperationException("لا يمكن حذف دور نظام محمي.");
            if (role.UserRoles.Count > 0)
                throw new InvalidOperationException("لا يمكن حذف دور مرتبط بمستخدمين. أزل التعيينات أولاً.");

            var oldState = Serialize(role);
            _dbContext.Roles.Remove(role);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(AuditActionKeys.RoleDeleted, nameof(Role), role.Id, oldState, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<List<Permission>> LoadPermissionsAsync(
        IReadOnlyCollection<string> requestedKeys,
        CancellationToken cancellationToken)
    {
        var keys = requestedKeys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (keys.Length == 0)
            throw new InvalidOperationException("يجب اختيار صلاحية واحدة على الأقل للدور.");

        var permissions = await _dbContext.Permissions.Where(item => keys.Contains(item.Key)).ToListAsync(cancellationToken);
        if (permissions.Count != keys.Length)
            throw new InvalidOperationException("توجد صلاحية محددة غير معروفة أو متوقفة.");
        PermissionPolicyCatalog.Validate(permissions.Select(item => item.Key).ToArray());
        return permissions;
    }

    private async Task<Role?> GetRoleGraphAsync(int id, CancellationToken cancellationToken)
        => await _dbContext.Roles
            .Include(role => role.RolePermissions).ThenInclude(item => item.Permission)
            .Include(role => role.UserRoles)
            .SingleOrDefaultAsync(role => role.Id == id, cancellationToken);

    private async Task EnsureAdministratorContinuityAsync(int roleId, CancellationToken cancellationToken)
    {
        var usersLosingAdministratorAccess = await _dbContext.Users
            .Where(user => user.IsActive && user.UserRoles.Any(item => item.RoleId == roleId))
            .Where(user => !user.IsSuperAdmin &&
                !user.UserPermissions.Any(item => item.Permission.Key == PermissionKeys.UsersChangePermissions) &&
                !user.UserRoles.Any(item => item.RoleId != roleId &&
                    item.Role.RolePermissions.Any(permission => permission.Permission.Key == PermissionKeys.UsersChangePermissions)))
            .Select(user => user.Id)
            .ToArrayAsync(cancellationToken);
        if (usersLosingAdministratorAccess.Length == 0) return;

        var anotherAdministratorExists = await _dbContext.Users.AnyAsync(user =>
            user.IsActive && !usersLosingAdministratorAccess.Contains(user.Id) &&
            (user.IsSuperAdmin ||
             user.UserPermissions.Any(item => item.Permission.Key == PermissionKeys.UsersChangePermissions) ||
             user.UserRoles.Any(item => item.Role.RolePermissions.Any(permission =>
                 permission.Permission.Key == PermissionKeys.UsersChangePermissions))),
            cancellationToken);
        if (!anotherAdministratorExists)
        {
            throw new InvalidOperationException("لا يمكن إزالة صلاحية إدارة المستخدمين من آخر مسؤول فعّال في النظام.");
        }
    }

    private static void ValidateRequest(SaveRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("اسم الدور مطلوب.");
        if (request.Name.Trim().Length is < 3 or > 120)
            throw new InvalidOperationException("اسم الدور يجب أن يكون من 3 إلى 120 حرفاً.");
        if (request.Description?.Length > 500)
            throw new InvalidOperationException("وصف الدور يجب ألا يتجاوز 500 حرف.");
    }

    private void ApplyExpectedRowVersion(Role role, string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) return;
        try
        {
            _dbContext.Entry(role).Property(item => item.RowVersion).OriginalValue = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("رمز تزامن الدور غير صالح. حدّث البيانات ثم أعد المحاولة.");
        }
    }

    private static RoleDetailsDto ToDto(Role role) => new(
        role.Id,
        role.Name,
        role.Description,
        role.IsSystem,
        role.IsProtected,
        role.RolePermissions.Select(item => item.Permission.Key).OrderBy(key => key).ToArray(),
        role.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(role.RowVersion));

    private static string Serialize(Role role) => JsonSerializer.Serialize(new
    {
        role.Name,
        role.Description,
        role.IsSystem,
        role.IsProtected,
        Permissions = role.RolePermissions.Select(item => item.Permission.Key).OrderBy(key => key)
    });

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
