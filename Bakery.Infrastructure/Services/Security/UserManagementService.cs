using System.Data;
using System.Text.Json;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class UserManagementService : IUserManagementService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPermissionService _permissionService;
    private readonly IUserSessionService _userSessionService;
    private readonly IAuditService _auditService;

    public UserManagementService(
        BakeryDbContext dbContext,
        IPasswordHasher passwordHasher,
        IPermissionService permissionService,
        IUserSessionService userSessionService,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _permissionService = permissionService;
        _userSessionService = userSessionService;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<UserListItemDto>> SearchAsync(
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersView);

        var query = _dbContext.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.Trim();
            query = query.Where(user => user.Username.Contains(search) || user.FullName.Contains(search));
        }

        return await query
            .OrderByDescending(user => user.IsActive)
            .ThenBy(user => user.FullName)
            .Select(user => new UserListItemDto(
                user.Id,
                user.Username,
                user.FullName,
                user.IsActive,
                user.UserPermissions.Select(item => item.PermissionId)
                    .Concat(user.UserRoles.SelectMany(item => item.Role.RolePermissions).Select(item => item.PermissionId))
                    .Distinct().Count(),
                user.CreatedAt,
                user.UpdatedAt,
                user.UserRoles.Select(item => item.Role.Name).OrderBy(name => name).ToArray()))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserDetailsDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersView);
        var user = await GetUserGraphAsync(id, cancellationToken);
        return user is null ? null : ToDetailsDto(user);
    }

    public async Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersChangePermissions);
        return await _dbContext.Permissions.AsNoTracking()
            .OrderBy(permission => permission.Category)
            .ThenBy(permission => permission.DisplayName)
            .Select(permission => new PermissionDto(
                permission.Id,
                permission.Key,
                permission.DisplayName,
                permission.Category))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserDetailsDto> CreateAsync(SaveUserRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersAdd);
        _permissionService.EnsurePermission(PermissionKeys.UsersChangePermissions);
        if ((request.RoleIds?.Count ?? 0) > 0)
        {
            _permissionService.EnsurePermission(PermissionKeys.RolesAssign);
        }
        ValidateSaveRequest(request, requirePassword: true);
        var passwordHash = await HashPasswordAsync(request.Password!, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var username = request.Username.Trim();
            var normalizedUsername = NormalizeName(username);
            if (await _dbContext.Users.IgnoreQueryFilters()
                .AnyAsync(user => user.NormalizedUsername == normalizedUsername || user.Username == username, cancellationToken))
            {
                throw new InvalidOperationException("اسم المستخدم مستخدم بالفعل أو مرتبط بحساب سابق.");
            }

            var selection = await LoadAndValidateSelectionAsync(request, cancellationToken);
            var user = new User
            {
                Username = username,
                NormalizedUsername = normalizedUsername,
                FullName = request.FullName.Trim(),
                PasswordHash = passwordHash,
                IsActive = request.IsActive,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                MustChangePassword = false
            };

            ApplySelection(user, selection);
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(
                AuditActionKeys.UserCreated,
                nameof(User),
                user.Id,
                null,
                SerializeSecurityState(user),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDetailsDto(user);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<UserDetailsDto> UpdateAsync(SaveUserRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersEdit);
        ValidateSaveRequest(request, requirePassword: false);
        var includesSecurityAssignments = request.PermissionKeys is not null ||
            request.BranchIds is not null || request.RoleIds is not null ||
            request.SafePermissions is not null;
        if (includesSecurityAssignments)
        {
            _permissionService.EnsurePermission(PermissionKeys.UsersChangePermissions);
        }
        if (request.RoleIds is not null)
        {
            _permissionService.EnsurePermission(PermissionKeys.RolesAssign);
        }
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            _permissionService.EnsurePermission(PermissionKeys.UsersResetPassword);
        }
        if (request.Id is null)
        {
            throw new InvalidOperationException("معرف المستخدم مطلوب.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var user = await GetUserGraphAsync(request.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("لم يتم العثور على المستخدم.");
            ApplyExpectedRowVersion(user, request.RowVersion);

            if (user.IsSuperAdmin && !_permissionService.IsAdmin())
            {
                throw new UnauthorizedAccessException("لا يمكن تعديل حساب مسؤول النظام إلا بواسطة مسؤول نظام آخر.");
            }

            var currentDetails = ToDetailsDto(user);
            var effectiveRequest = request with
            {
                PermissionKeys = request.PermissionKeys ?? currentDetails.PermissionKeys,
                BranchIds = request.BranchIds ?? currentDetails.BranchIds,
                RoleIds = request.RoleIds ?? currentDetails.RoleIds,
                SafePermissions = request.SafePermissions ?? currentDetails.SafePermissions
            };

            var rolesChanged = !user.UserRoles.Select(item => item.RoleId).ToHashSet()
                .SetEquals(effectiveRequest.RoleIds ?? []);
            var securitySelectionChanged =
                !user.UserPermissions.Select(item => item.Permission.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(effectiveRequest.PermissionKeys ?? []) ||
                !user.UserBranches.Select(item => item.BranchId).ToHashSet()
                    .SetEquals(effectiveRequest.BranchIds ?? []) ||
                rolesChanged ||
                !SafeSelectionsEqual(user.UserSafePermissions, effectiveRequest.SafePermissions ?? []);



            var username = request.Username.Trim();
            var normalizedUsername = NormalizeName(username);
            if (await _dbContext.Users.IgnoreQueryFilters().AnyAsync(
                existing => existing.Id != user.Id &&
                    (existing.NormalizedUsername == normalizedUsername || existing.Username == username),
                cancellationToken))
            {
                throw new InvalidOperationException("اسم المستخدم مستخدم بالفعل أو مرتبط بحساب سابق.");
            }

            SecuritySelection? selection = null;
            if (securitySelectionChanged)
            {
                selection = await LoadAndValidateSelectionAsync(effectiveRequest, cancellationToken);
            }
            if (user.Id == _userSessionService.UserId &&
                (!request.IsActive || securitySelectionChanged))
            {
                throw new InvalidOperationException("لا يمكنك تغيير صلاحيات أو فروع أو خزائن حسابك أثناء استخدامه.");
            }

            var wasAdministrator = IsEffectiveAdministrator(user);
            var willBeAdministrator = selection is null
                ? wasAdministrator
                : user.IsSuperAdmin || selection.EffectivePermissionKeys.Contains(PermissionKeys.UsersChangePermissions);
            if (wasAdministrator && (!request.IsActive || !willBeAdministrator))
            {
                await EnsureAnotherEffectiveAdministratorExistsAsync(user.Id, cancellationToken);
            }

            var oldState = SerializeSecurityState(user);
            user.Username = username;
            user.NormalizedUsername = normalizedUsername;
            user.FullName = request.FullName.Trim();
            user.IsActive = request.IsActive;
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                ValidatePassword(request.Password);
                user.PasswordHash = await HashPasswordAsync(request.Password, cancellationToken);
                user.MustChangePassword = false;
            }

            if (selection is not null)
            {
                ReplaceSelection(user, selection);
            }
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(
                AuditActionKeys.UserUpdated,
                nameof(User),
                user.Id,
                oldState,
                SerializeSecurityState(user),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            if (user.Id == _userSessionService.UserId)
            {
                _userSessionService.Refresh(ToAuthenticatedUser(user));
            }
            return ToDetailsDto(user);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("تم تعديل المستخدم بواسطة جلسة أخرى. حدّث البيانات ثم أعد المحاولة.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task SetActiveAsync(int userId, bool isActive, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersEdit);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var user = await GetUserGraphAsync(userId, cancellationToken)
                ?? throw new InvalidOperationException("لم يتم العثور على المستخدم.");
            if (!isActive)
            {
                EnsureNotCurrentUser(userId);
                if (IsEffectiveAdministrator(user))
                {
                    await EnsureAnotherEffectiveAdministratorExistsAsync(userId, cancellationToken);
                }
            }

            var oldValue = user.IsActive.ToString();
            user.IsActive = isActive;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(AuditActionKeys.UserActiveStateChanged, nameof(User), user.Id, oldValue, isActive.ToString(), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _userSessionService.InvalidateIfCurrentUser(userId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersResetPassword);
        ValidatePassword(request.NewPassword);
        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Id == request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("لم يتم العثور على المستخدم.");

        if (user.IsSuperAdmin && !_permissionService.IsAdmin())
        {
            throw new UnauthorizedAccessException("لا يمكن إعادة تعيين كلمة مرور مسؤول النظام.");
        }

        user.PasswordHash = await HashPasswordAsync(request.NewPassword, cancellationToken);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            AuditActionKeys.UserPasswordReset,
            nameof(User),
            user.Id,
            null,
            "Password reset by administrator",
            cancellationToken);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var userId = _userSessionService.UserId
            ?? throw new UnauthorizedAccessException("يجب تسجيل الدخول أولاً.");
        ValidatePassword(request.NewPassword);
        var user = await GetUserGraphAsync(userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("انتهت جلسة المستخدم.");
        if (!await VerifyPasswordAsync(request.CurrentPassword, user.PasswordHash, cancellationToken))
        {
            throw new InvalidOperationException("كلمة المرور الحالية غير صحيحة.");
        }

        user.PasswordHash = await HashPasswordAsync(request.NewPassword, cancellationToken);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(AuditActionKeys.UserPasswordChanged, nameof(User), user.Id, null, null, cancellationToken);
        _userSessionService.Refresh(ToAuthenticatedUser(user));
    }

    public async Task<bool> CanDeleteAsync(int userId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersDelete);
        if (userId == _userSessionService.UserId)
        {
            return false;
        }

        var user = await GetUserGraphAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }
        if (!IsEffectiveAdministrator(user))
        {
            return true;
        }
        return await AnotherEffectiveAdministratorExistsAsync(userId, cancellationToken);
    }

    public async Task DeleteAsync(int userId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersDelete);
        EnsureNotCurrentUser(userId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var user = await GetUserGraphAsync(userId, cancellationToken)
                ?? throw new InvalidOperationException("لم يتم العثور على المستخدم.");
            if (IsEffectiveAdministrator(user))
            {
                await EnsureAnotherEffectiveAdministratorExistsAsync(userId, cancellationToken);
            }
            if (user.IsSuperAdmin && !_permissionService.IsAdmin())
            {
                throw new UnauthorizedAccessException("لا يمكن حذف حساب مسؤول النظام.");
            }

            var oldState = SerializeSecurityState(user);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(AuditActionKeys.UserDeleted, nameof(User), user.Id, oldState, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<SecuritySelection> LoadAndValidateSelectionAsync(
        SaveUserRequest request,
        CancellationToken cancellationToken)
    {
        var keys = (request.PermissionKeys ?? []).Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var permissions = await _dbContext.Permissions.Where(permission => keys.Contains(permission.Key)).ToListAsync(cancellationToken);
        if (permissions.Count != keys.Length)
        {
            throw new InvalidOperationException("توجد صلاحية محددة غير معروفة أو متوقفة.");
        }

        var branchIds = (request.BranchIds ?? []).Distinct().ToArray();
        var branches = await _dbContext.Branches.IgnoreQueryFilters()
            .Where(branch => branchIds.Contains(branch.Id) && !branch.IsDeleted && branch.IsActive)
            .ToListAsync(cancellationToken);
        if (branches.Count != branchIds.Length)
        {
            throw new InvalidOperationException("يوجد فرع محدد غير موجود أو غير نشط.");
        }

        var roleIds = (request.RoleIds ?? []).Distinct().ToArray();
        var roles = await _dbContext.Roles
            .Include(role => role.RolePermissions).ThenInclude(item => item.Permission)
            .Where(role => roleIds.Contains(role.Id))
            .ToListAsync(cancellationToken);
        if (roles.Count != roleIds.Length)
        {
            throw new InvalidOperationException("يوجد دور أمني محدد غير موجود.");
        }

        var safeDtos = request.SafePermissions?.ToArray() ?? [];
        if (safeDtos.Select(item => item.SafeId).Distinct().Count() != safeDtos.Length)
        {
            throw new InvalidOperationException("لا يمكن تكرار الخزينة في صلاحيات المستخدم.");
        }
        if (safeDtos.Any(item => !item.CanAccess &&
            (item.CanViewBalance || item.CanViewLedger || item.CanCashIn || item.CanCashOut ||
             item.CanTransferFrom || item.CanReceiveTransfer)))
        {
            throw new InvalidOperationException("يجب منح الوصول إلى الخزينة قبل منح أي عملية عليها.");
        }

        var safeIds = safeDtos.Select(item => item.SafeId).ToArray();
        var safes = await _dbContext.Safes.IgnoreQueryFilters()
            .Where(safe => safeIds.Contains(safe.Id) && !safe.IsDeleted && safe.IsActive)
            .ToListAsync(cancellationToken);
        if (safes.Count != safeIds.Length || safes.Any(safe => !branchIds.Contains(safe.BranchId)))
        {
            throw new InvalidOperationException("الخزائن المحددة يجب أن تكون نشطة وضمن فروع المستخدم.");
        }

        var effectiveKeys = permissions.Select(permission => permission.Key)
            .Concat(roles.SelectMany(role => role.RolePermissions).Select(item => item.Permission.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        PermissionPolicyCatalog.Validate(effectiveKeys);
        if (effectiveKeys.Count == 0)
        {
            throw new InvalidOperationException("يجب اختيار صلاحية مباشرة أو دور أمني واحد على الأقل.");
        }

        return new SecuritySelection(permissions, branches, roles, safes, safeDtos, effectiveKeys);
    }

    private static void ApplySelection(User user, SecuritySelection selection)
    {
        foreach (var permission in selection.Permissions)
            user.UserPermissions.Add(new UserPermission { Permission = permission });
        foreach (var branch in selection.Branches)
            user.UserBranches.Add(new UserBranch { Branch = branch });
        foreach (var role in selection.Roles)
            user.UserRoles.Add(new UserRole { Role = role });
        foreach (var safeDto in selection.SafePermissions.Where(item => item.CanAccess))
        {
            var safe = selection.Safes.Single(item => item.Id == safeDto.SafeId);
            user.UserSafePermissions.Add(ToSafePermission(safeDto, safe));
        }
    }

    private static void ReplaceSelection(User user, SecuritySelection selection)
    {
        user.UserPermissions.Clear();
        user.UserBranches.Clear();
        user.UserRoles.Clear();
        user.UserSafePermissions.Clear();
        ApplySelection(user, selection);
    }

    private static UserSafePermission ToSafePermission(UserSafePermissionDto dto, Safe safe) => new()
    {
        Safe = safe,
        BranchId = safe.BranchId,
        CanAccess = true,
        CanViewBalance = dto.CanViewBalance,
        CanViewLedger = dto.CanViewLedger,
        CanCashIn = dto.CanCashIn,
        CanCashOut = dto.CanCashOut,
        CanTransferFrom = dto.CanTransferFrom,
        CanReceiveTransfer = dto.CanReceiveTransfer
    };

    private static bool SafeSelectionsEqual(
        IEnumerable<UserSafePermission> existing,
        IEnumerable<UserSafePermissionDto> requested)
    {
        var left = existing.Where(item => item.CanAccess).OrderBy(item => item.SafeId)
            .Select(item => $"{item.SafeId}:{item.CanViewBalance}:{item.CanViewLedger}:{item.CanCashIn}:{item.CanCashOut}:{item.CanTransferFrom}:{item.CanReceiveTransfer}");
        var right = requested.Where(item => item.CanAccess).OrderBy(item => item.SafeId)
            .Select(item => $"{item.SafeId}:{item.CanViewBalance}:{item.CanViewLedger}:{item.CanCashIn}:{item.CanCashOut}:{item.CanTransferFrom}:{item.CanReceiveTransfer}");
        return left.SequenceEqual(right);
    }

    private async Task<User?> GetUserGraphAsync(int userId, CancellationToken cancellationToken)
        => await _dbContext.Users
            .Include(user => user.UserPermissions).ThenInclude(item => item.Permission)
            .Include(user => user.UserBranches)
            .Include(user => user.UserRoles).ThenInclude(item => item.Role)
                .ThenInclude(role => role.RolePermissions).ThenInclude(item => item.Permission)
            .Include(user => user.UserSafePermissions).ThenInclude(item => item.Safe)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    private static bool IsEffectiveAdministrator(User user)
        => user.IsSuperAdmin || user.UserPermissions.Any(item => item.Permission.Key == PermissionKeys.UsersChangePermissions) ||
           user.UserRoles.Any(item => item.Role.RolePermissions.Any(rolePermission =>
               rolePermission.Permission.Key == PermissionKeys.UsersChangePermissions));

    private async Task<bool> AnotherEffectiveAdministratorExistsAsync(int excludedUserId, CancellationToken cancellationToken)
        => await _dbContext.Users.AnyAsync(user =>
            user.Id != excludedUserId && user.IsActive &&
            (user.IsSuperAdmin ||
             user.UserPermissions.Any(item => item.Permission.Key == PermissionKeys.UsersChangePermissions) ||
             user.UserRoles.Any(item => item.Role.RolePermissions.Any(rolePermission =>
                 rolePermission.Permission.Key == PermissionKeys.UsersChangePermissions))),
            cancellationToken);

    private async Task EnsureAnotherEffectiveAdministratorExistsAsync(int excludedUserId, CancellationToken cancellationToken)
    {
        if (!await AnotherEffectiveAdministratorExistsAsync(excludedUserId, cancellationToken))
        {
            throw new InvalidOperationException("يجب أن يظل مسؤول نظام نشط واحد على الأقل.");
        }
    }

    private void EnsureNotCurrentUser(int userId)
    {
        if (userId == _userSessionService.UserId)
        {
            throw new InvalidOperationException("لا يمكنك تعطيل أو حذف الحساب الذي تستخدمه حالياً.");
        }
    }

    private static void ValidateSaveRequest(SaveUserRequest request, bool requirePassword)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            throw new InvalidOperationException("اسم المستخدم مطلوب.");
        if (request.Username.Trim().Length is < 3 or > 100 ||
            request.Username.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("اسم المستخدم يجب أن يكون من 3 إلى 100 حرف وبدون مسافات.");
        if (string.IsNullOrWhiteSpace(request.FullName))
            throw new InvalidOperationException("الاسم الكامل مطلوب.");
        if (request.FullName.Trim().Length > 150)
            throw new InvalidOperationException("الاسم الكامل يجب ألا يتجاوز 150 حرفاً.");
        if (requirePassword || !string.IsNullOrWhiteSpace(request.Password))
            ValidatePassword(request.Password);
        if ((requirePassword && request.BranchIds is null) || request.BranchIds is { Count: 0 })
            throw new InvalidOperationException("يجب اختيار فرع واحد على الأقل.");
    }

    private static void ValidatePassword(string? password)
    {
        PasswordPolicy.EnsureValid(password);
    }

    private Task<string> HashPasswordAsync(string password, CancellationToken cancellationToken) =>
        Task.Run(() => _passwordHasher.HashPassword(password), cancellationToken);

    private Task<bool> VerifyPasswordAsync(
        string password,
        string passwordHash,
        CancellationToken cancellationToken) =>
        Task.Run(() => _passwordHasher.VerifyPassword(password, passwordHash), cancellationToken);

    private void ApplyExpectedRowVersion(User user, string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            return;
        try
        {
            _dbContext.Entry(user).Property(item => item.RowVersion).OriginalValue = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("رمز تزامن المستخدم غير صالح. حدّث البيانات ثم أعد المحاولة.");
        }
    }

    private static UserDetailsDto ToDetailsDto(User user) => new(
        user.Id,
        user.Username,
        user.FullName,
        user.IsActive,
        user.UserPermissions.Select(item => item.Permission.Key).OrderBy(key => key).ToArray(),
        user.UserBranches.Select(item => item.BranchId).OrderBy(id => id).ToArray(),
        user.UserRoles.Select(item => item.RoleId).OrderBy(id => id).ToArray(),
        user.UserSafePermissions.Where(item => item.CanAccess).Select(item => new UserSafePermissionDto
        {
            Id = item.Id,
            UserId = user.Id,
            SafeId = item.SafeId,
            SafeName = !string.IsNullOrWhiteSpace(item.Safe.ArabicName) ? item.Safe.ArabicName : item.Safe.Name,
            CanAccess = item.CanAccess,
            CanViewBalance = item.CanViewBalance,
            CanViewLedger = item.CanViewLedger,
            CanCashIn = item.CanCashIn,
            CanCashOut = item.CanCashOut,
            CanTransferFrom = item.CanTransferFrom,
            CanReceiveTransfer = item.CanReceiveTransfer
        }).ToArray(),
        user.RowVersion.Length == 0 ? null : Convert.ToBase64String(user.RowVersion),
        false);

    private static AuthenticatedUserDto ToAuthenticatedUser(User user)
    {
        var permissions = user.UserPermissions.Select(item => item.Permission.Key)
            .Concat(user.UserRoles.SelectMany(item => item.Role.RolePermissions).Select(item => item.Permission.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new AuthenticatedUserDto(
            user.Id, user.Username, user.FullName, permissions, user.IsSuperAdmin,
            user.SecurityStamp, false,
            user.UserRoles.Select(item => item.Role.Name).OrderBy(name => name).ToArray());
    }

    private static string SerializeSecurityState(User user) => JsonSerializer.Serialize(new
    {
        user.Username,
        user.FullName,
        user.IsActive,
        user.IsSuperAdmin,
        Permissions = user.UserPermissions.Select(item => item.Permission.Key).OrderBy(key => key),
        Roles = user.UserRoles.Select(item => item.Role.Name).OrderBy(name => name),
        Branches = user.UserBranches.Select(item => item.BranchId).OrderBy(id => id),
        Safes = user.UserSafePermissions.Where(item => item.CanAccess).Select(item => item.SafeId).OrderBy(id => id)
    });

    private static string NormalizeName(string value) => value.Trim().ToUpperInvariant();

    private sealed record SecuritySelection(
        IReadOnlyList<Permission> Permissions,
        IReadOnlyList<Branch> Branches,
        IReadOnlyList<Role> Roles,
        IReadOnlyList<Safe> Safes,
        IReadOnlyList<UserSafePermissionDto> SafePermissions,
        HashSet<string> EffectivePermissionKeys);
}
