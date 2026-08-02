using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Bakery.Shared.Helpers;

namespace Bakery.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private const int MaximumFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private readonly BakeryDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUserSessionService _userSessionService;
    private readonly IInternalBranchContext _branchContext;
    private readonly IInternalSafeContext _safeContext;
    private readonly IAuditService _auditService;
    private readonly IValidator<LoginRequest> _validator;

    public AuthService(
        BakeryDbContext dbContext,
        IPasswordHasher passwordHasher,
        IUserSessionService userSessionService,
        IBranchContext branchContext,
        ISafeContext safeContext,
        IAuditService auditService,
        IValidator<LoginRequest> validator)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _userSessionService = userSessionService;
        _branchContext = branchContext.AsInternal();
        _safeContext = safeContext.AsInternal();
        _auditService = auditService;
        _validator = validator;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        // A login attempt always starts from a clean session. This prevents a failed
        // re-login from leaving a previously authenticated identity active.
        _userSessionService.SignOut();
        _branchContext.Clear();
        _safeContext.Clear();

        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return new AuthResult(false, validationResult.Errors[0].ErrorMessage, null);
        }

        var username = request.UserName.Trim();
        var normalizedUsername = username.ToUpperInvariant();
        var credential = await _dbContext.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entity =>
                entity.NormalizedUsername == normalizedUsername || entity.Username == username)
            .Select(entity => new LoginCredential(
                entity.Id,
                entity.Username,
                entity.FullName,
                entity.PasswordHash,
                entity.IsActive,
                entity.IsDeleted,
                entity.IsSuperAdmin,
                entity.SecurityStamp,
                entity.FailedLoginCount,
                entity.LockoutEndUtc))
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var canVerifyPassword = credential is not null &&
            !credential.IsDeleted &&
            credential.IsActive &&
            (credential.LockoutEndUtc is null || credential.LockoutEndUtc <= now);
        var passwordIsValid = canVerifyPassword &&
            await Task.Run(
                () => _passwordHasher.VerifyPassword(request.Password, credential!.PasswordHash),
                cancellationToken);

        if (!passwordIsValid)
        {
            if (canVerifyPassword)
            {
                var failedLoginCount = credential!.FailedLoginCount + 1;
                var lockoutEndUtc = failedLoginCount >= MaximumFailedAttempts
                    ? now.Add(LockoutDuration)
                    : credential.LockoutEndUtc;
                if (failedLoginCount >= MaximumFailedAttempts) failedLoginCount = 0;

                await _dbContext.Users
                    .IgnoreQueryFilters()
                    .Where(entity => entity.Id == credential.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(entity => entity.FailedLoginCount, failedLoginCount)
                        .SetProperty(entity => entity.LockoutEndUtc, lockoutEndUtc)
                        .SetProperty(entity => entity.UpdatedAt, now)
                        .SetProperty(entity => entity.UpdatedBy, "system"),
                        cancellationToken);
            }
            await _auditService.LogAsync(AuditActionKeys.LoginFailed, "User", null, null, request.UserName, cancellationToken);
            _branchContext.Clear();
            _safeContext.Clear();
            return new AuthResult(false, Loc.ErrInvalidCredentials, null);
        }

        var authenticatedCredential = credential!;

        // Credential verification deliberately happens before these small, read-only
        // authorization projections. No sibling collections or entity graph are materialized.
        var directAuthorizations = _dbContext.UserPermissions
            .AsNoTracking()
            .Where(item => item.UserId == authenticatedCredential.Id)
            .Select(item => new
            {
                RoleName = (string?)null,
                PermissionKey = (string?)item.Permission.Key
            });
        var assignedRoles = _dbContext.UserRoles
            .AsNoTracking()
            .Where(item => item.UserId == authenticatedCredential.Id && !item.Role.IsDeleted);
        var roleMarkers = assignedRoles
            .Select(item => new
            {
                RoleName = (string?)item.Role.Name,
                PermissionKey = (string?)null
            });
        var roleAuthorizations = _dbContext.RolePermissions
            .AsNoTracking()
            .Where(rolePermission => assignedRoles.Any(userRole => userRole.RoleId == rolePermission.RoleId))
            .Select(rolePermission => new
            {
                RoleName = (string?)rolePermission.Role.Name,
                PermissionKey = (string?)rolePermission.Permission.Key
            });
        var authorizationRows = await directAuthorizations
            .Concat(roleMarkers)
            .Concat(roleAuthorizations)
            .ToArrayAsync(cancellationToken);
        var effectivePermissions = authorizationRows
            .Where(item => item.PermissionKey is not null)
            .Select(item => item.PermissionKey!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var roleNames = authorizationRows
            .Where(item => item.RoleName is not null)
            .Select(item => item.RoleName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToArray();

        var availableBranches = await _dbContext.UserBranches
            .AsNoTracking()
            .Where(item => item.UserId == authenticatedCredential.Id &&
                item.Branch.IsActive && !item.Branch.IsDeleted)
            .OrderBy(item => item.Branch.Name)
            .Select(item => new BranchDto(
                item.Branch.Id,
                item.Branch.Code,
                item.Branch.Name,
                item.Branch.IsActive,
                item.Branch.Notes))
            .ToArrayAsync(cancellationToken);

        var selectedBranchId = request.BranchId;
        if (selectedBranchId is null)
        {
            selectedBranchId = availableBranches
                .Select(branch => (int?)branch.Id)
                .FirstOrDefault();
        }

        var selectedBranch = availableBranches.FirstOrDefault(branch => branch.Id == selectedBranchId);

        if (selectedBranch is null)
        {
            _branchContext.Clear();
            _safeContext.Clear();
            return new AuthResult(false, Loc.NoBranchesAssigned, null);
        }

        await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(entity => entity.Id == authenticatedCredential.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entity => entity.FailedLoginCount, 0)
                .SetProperty(entity => entity.LockoutEndUtc, (DateTime?)null)
                .SetProperty(entity => entity.LastLoginAtUtc, now)
                .SetProperty(entity => entity.UpdatedAt, now)
                .SetProperty(entity => entity.UpdatedBy, "system"),
                cancellationToken);

        var authenticatedUser = new AuthenticatedUserDto(
            authenticatedCredential.Id,
            authenticatedCredential.Username,
            authenticatedCredential.FullName,
            effectivePermissions,
            authenticatedCredential.IsSuperAdmin,
            authenticatedCredential.SecurityStamp,
            false,
            roleNames);

        var selectedSafe = await _dbContext.Safes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.BranchId == selectedBranch.Id &&
                s.IsActive &&
                !s.IsDeleted &&
                (authenticatedCredential.IsSuperAdmin || _dbContext.UserSafePermissions.Any(permission =>
                    permission.UserId == authenticatedCredential.Id &&
                    permission.SafeId == s.Id &&
                    permission.CanAccess)))
            .OrderByDescending(s => s.Type == Domain.Enums.SafeType.Daily)
            .ThenByDescending(s => s.Type == Domain.Enums.SafeType.Main)
            .ThenBy(s => s.Name)
            .Select(s => new SafeDto(s.Id, s.Name, s.ArabicName, 0, s.Type, null))
            .FirstOrDefaultAsync(cancellationToken);

        try
        {
            _branchContext.ConfigureBranch(selectedBranch);
            if (selectedSafe is not null)
            {
                _safeContext.ConfigureSafe(selectedSafe);
            }
            else
            {
                _safeContext.Clear();
            }

            _userSessionService.SignIn(authenticatedUser);
            await _auditService.LogAsync(AuditActionKeys.Login, "User", authenticatedCredential.Id, null, authenticatedCredential.Username, cancellationToken);
            return new AuthResult(true, null, authenticatedUser, availableBranches);
        }
        catch
        {
            _branchContext.Clear();
            _safeContext.Clear();
            _userSessionService.SignOut();
            throw;
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var user = _userSessionService.CurrentUser;
        if (user is not null)
        {
            await _auditService.LogAsync(AuditActionKeys.Logout, "User", user.UserId, null, user.UserName, cancellationToken);
        }

        _branchContext.Clear();
        _safeContext.Clear();
        _userSessionService.SignOut();
    }

    public async Task<IReadOnlyList<BranchDto>> GetActiveBranchesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Branches
            .AsNoTracking()
            .Where(b => b.IsActive && !b.IsDeleted)
            .OrderBy(b => b.Name)
            .Select(b => new BranchDto(b.Id, b.Code, b.Name, b.IsActive, b.Notes))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersForBranchAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserBranches
            .AsNoTracking()
            .Where(ub => ub.BranchId == branchId &&
                         ub.Branch.IsActive && !ub.Branch.IsDeleted &&
                         ub.User.IsActive && !ub.User.IsDeleted)
            .OrderBy(ub => ub.User.FullName)
            .Select(ub => new UserDto(ub.User.Id, ub.User.Username, ub.User.FullName))
            .ToListAsync(cancellationToken);
    }

    private sealed record LoginCredential(
        int Id,
        string Username,
        string FullName,
        string PasswordHash,
        bool IsActive,
        bool IsDeleted,
        bool IsSuperAdmin,
        string SecurityStamp,
        int FailedLoginCount,
        DateTime? LockoutEndUtc);

}
