using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services;

public sealed class PermissionService : IPermissionService
{
    private readonly IUserSessionService _userSessionService;
    private readonly BakeryDbContext? _dbContext;
    private readonly ILogger<PermissionService> _logger;
    private readonly IBranchContext? _branchContext;

    public PermissionService(
        IUserSessionService userSessionService,
        BakeryDbContext dbContext,
        IBranchContext branchContext,
        ILogger<PermissionService> logger)
    {
        _userSessionService = userSessionService;
        _dbContext = dbContext;
        _branchContext = branchContext;
        _logger = logger;
    }

    public PermissionService(
        IUserSessionService userSessionService,
        ILogger<PermissionService> logger)
    {
        _userSessionService = userSessionService;
        _logger = logger;
    }

    public bool HasPermission(string permissionKey)
    {
        return _userSessionService.HasPermission(permissionKey);
    }

    public bool HasAnyPermission(params string[] permissionKeys)
        => permissionKeys.Any(HasPermission);

    public void EnsurePermission(string permissionKey)
    {
        EnsureSessionIsCurrent();
        if (!HasPermission(permissionKey))
        {
            var userId = _userSessionService.UserId;
            var username = _userSessionService.Username ?? "Unknown";
            _logger.LogWarning("Security Violation: User '{Username}' (ID: {UserId}) attempted an unauthorized action requiring permission '{PermissionKey}'.", username, userId, permissionKey);
            RecordDeniedAuthorization(permissionKey);
            
            throw new UnauthorizedAccessException("ليس لديك صلاحية لتنفيذ هذا الإجراء.");
        }
    }

    public void EnsureAnyPermission(params string[] permissionKeys)
    {
        EnsureSessionIsCurrent();
        if (!HasAnyPermission(permissionKeys))
        {
            var userId = _userSessionService.UserId;
            _logger.LogWarning(
                "Security Violation: User ID {UserId} attempted an action requiring one of {PermissionKeys}.",
                userId,
                string.Join(",", permissionKeys));
            RecordDeniedAuthorization(string.Join(" | ", permissionKeys));
            throw new UnauthorizedAccessException("ليس لديك صلاحية لتنفيذ هذا الإجراء.");
        }
    }

    public bool IsAdmin()
    {
        return _userSessionService.IsSuperAdmin;
    }

    private void RecordDeniedAuthorization(string requiredPermissions)
    {
        if (_dbContext is null) return;

        try
        {
            var connectionString = _dbContext.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString)) return;
            var options = new DbContextOptionsBuilder<BakeryDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            using var auditDb = new BakeryDbContext(options);
            var requestedBranchId = _branchContext?.CurrentBranchId;
            var branchId = requestedBranchId.HasValue && auditDb.Branches.IgnoreQueryFilters()
                    .Any(branch => branch.Id == requestedBranchId.Value)
                ? requestedBranchId.Value
                : auditDb.Branches.IgnoreQueryFilters()
                    .OrderBy(branch => branch.Id)
                    .Select(branch => branch.Id)
                    .FirstOrDefault();
            if (branchId == 0) return;

            var userId = _userSessionService.UserId;
            if (userId.HasValue && !auditDb.Users.IgnoreQueryFilters()
                    .Any(user => user.Id == userId.Value))
            {
                userId = null;
            }

            auditDb.AuditLogs.Add(new AuditLog
            {
                BranchId = branchId,
                UserId = userId,
                Action = AuditActionKeys.AuthorizationDenied,
                EntityName = "Permission",
                NewValues = $"RequiredPermissions={requiredPermissions}",
                MachineName = Environment.MachineName,
                OccurredAt = DateTime.UtcNow
            });
            auditDb.SaveChanges();
        }
        catch (Exception exception)
        {
            // Authorization must remain denied even if the audit store is unavailable.
            _logger.LogError(exception, "Failed to persist an authorization-denied audit event.");
        }
    }

    private void EnsureSessionIsCurrent()
    {
        var session = _userSessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("انتهت جلسة المستخدم. يرجى تسجيل الدخول مرة أخرى.");

        // Legacy/test sessions created without a security stamp remain compatible.
        // Every production login supplies a persisted stamp and is validated here.
        if (string.IsNullOrWhiteSpace(session.SecurityStamp) || _dbContext is null)
        {
            return;
        }

        var state = _dbContext.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(user => user.Id == session.UserId)
            .Select(user => new { user.IsActive, user.IsDeleted, user.SecurityStamp })
            .SingleOrDefault();

        if (state is null || !state.IsActive || state.IsDeleted ||
            !string.Equals(state.SecurityStamp, session.SecurityStamp, StringComparison.Ordinal))
        {
            _userSessionService.InvalidateIfCurrentUser(session.UserId);
            throw new UnauthorizedAccessException("تم تحديث صلاحيات حسابك أو إنهاء جلستك. يرجى تسجيل الدخول مرة أخرى.");
        }
    }
}
