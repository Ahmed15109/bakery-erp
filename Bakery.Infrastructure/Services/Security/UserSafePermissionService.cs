using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Bakery.Application.Security;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Bakery.Infrastructure.Services;

public sealed class UserSafePermissionService : IUserSafePermissionService
{
    private readonly BakeryDbContext _db;
    private readonly IPermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly IUserSessionService _userSessionService;

    public UserSafePermissionService(
        BakeryDbContext db,
        IPermissionService permissionService,
        IAuditService auditService,
        IUserSessionService userSessionService)
    {
        _db = db;
        _permissionService = permissionService;
        _auditService = auditService;
        _userSessionService = userSessionService;
    }

    public async Task<GetUserSafePermissionsResponse> GetUserPermissionsAsync(int userId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersChangePermissions);
        var safes = await _db.Safes
            .Where(s => !s.IsDeleted && s.IsActive)
            .OrderByDescending(s => s.Code == "DAILY_CASH_SAFE")
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var isSuperAdmin = await _db.Users.AnyAsync(u => u.Id == userId && u.IsSuperAdmin, cancellationToken);
        var hasConfigured = await _db.UserSafePermissions.AnyAsync(p => p.UserId == userId, cancellationToken);
        var existing = await _db.UserSafePermissions
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.SafeId, cancellationToken);

        var list = new List<UserSafePermissionDto>();
        foreach (var safe in safes)
        {
            var safeName = !string.IsNullOrWhiteSpace(safe.ArabicName) ? safe.ArabicName : safe.Name;
            if (!hasConfigured)
            {
                list.Add(new UserSafePermissionDto
                {
                    UserId = userId,
                    SafeId = safe.Id,
                    SafeName = safeName,
                    CanAccess = isSuperAdmin,
                    CanViewBalance = isSuperAdmin,
                    CanViewLedger = isSuperAdmin,
                    CanCashIn = isSuperAdmin,
                    CanCashOut = isSuperAdmin,
                    CanTransferFrom = isSuperAdmin,
                    CanReceiveTransfer = isSuperAdmin
                });
            }
            else
            {
                if (existing.TryGetValue(safe.Id, out var perm))
                {
                    list.Add(new UserSafePermissionDto
                    {
                        Id = perm.Id,
                        UserId = userId,
                        SafeId = safe.Id,
                        SafeName = safeName,
                        CanAccess = perm.CanAccess,
                        CanViewBalance = perm.CanViewBalance,
                        CanViewLedger = perm.CanViewLedger,
                        CanCashIn = perm.CanCashIn,
                        CanCashOut = perm.CanCashOut,
                        CanTransferFrom = perm.CanTransferFrom,
                        CanReceiveTransfer = perm.CanReceiveTransfer
                    });
                }
                else
                {
                    list.Add(new UserSafePermissionDto
                    {
                        UserId = userId,
                        SafeId = safe.Id,
                        SafeName = safeName,
                        CanAccess = false,
                        CanViewBalance = false,
                        CanViewLedger = false,
                        CanCashIn = false,
                        CanCashOut = false,
                        CanTransferFrom = false,
                        CanReceiveTransfer = false
                    });
                }
            }
        }

        return new GetUserSafePermissionsResponse(userId, list);
    }

    public async Task UpdateUserPermissionsAsync(UpdateUserSafePermissionsRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.UsersChangePermissions);
        if (_userSessionService.UserId == request.UserId)
        {
            throw new InvalidOperationException("لا يمكنك تغيير صلاحيات خزائن حسابك الحالي أثناء استخدامه.");
        }
        var userExists = await _db.Users.AnyAsync(u => u.Id == request.UserId && !u.IsDeleted, cancellationToken);
        if (!userExists)
        {
            throw new InvalidOperationException($"User with ID {request.UserId} does not exist.");
        }

        var safeIds = request.Permissions.Select(p => p.SafeId).ToList();
        if (safeIds.Count != safeIds.Distinct().Count())
        {
            throw new InvalidOperationException("Duplicate SafeId rows are not allowed.");
        }

        var validSafeIds = await _db.Safes
            .Where(safe => safeIds.Contains(safe.Id) && !safe.IsDeleted && safe.IsActive)
            .Select(safe => safe.Id)
            .ToListAsync(cancellationToken);
        if (validSafeIds.Count != safeIds.Count)
        {
            throw new InvalidOperationException("توجد خزينة محددة غير موجودة أو غير نشطة.");
        }

        if (request.Permissions.Any(item => !item.CanAccess &&
            (item.CanViewBalance || item.CanViewLedger || item.CanCashIn || item.CanCashOut ||
             item.CanTransferFrom || item.CanReceiveTransfer)))
        {
            throw new InvalidOperationException("يجب منح الوصول إلى الخزينة قبل منح أي عملية عليها.");
        }

        var existing = await _db.UserSafePermissions
            .Where(p => p.UserId == request.UserId)
            .ToListAsync(cancellationToken);
        var oldValue = JsonSerializer.Serialize(existing.Select(ToAuditState));
        _db.UserSafePermissions.RemoveRange(existing);

        foreach (var dto in request.Permissions.Where(item => item.CanAccess))
        {
            _db.UserSafePermissions.Add(new UserSafePermission
            {
                UserId = request.UserId,
                SafeId = dto.SafeId,
                CanAccess = dto.CanAccess,
                CanViewBalance = dto.CanViewBalance,
                CanViewLedger = dto.CanViewLedger,
                CanCashIn = dto.CanCashIn,
                CanCashOut = dto.CanCashOut,
                CanTransferFrom = dto.CanTransferFrom,
                CanReceiveTransfer = dto.CanReceiveTransfer
            });
        }

        var user = await _db.Users.SingleAsync(item => item.Id == request.UserId, cancellationToken);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            AuditActionKeys.UserSafePermissionsUpdated,
            nameof(User),
            request.UserId,
            oldValue,
            JsonSerializer.Serialize(request.Permissions.Where(item => item.CanAccess)),
            cancellationToken);
    }

    private async Task<bool> CheckPermissionAsync(int userId, int safeId, Func<UserSafePermission, bool> predicate, CancellationToken cancellationToken)
    {
        if (userId <= 0) return false;

        var isSuperAdmin = await _db.Users.AnyAsync(u => u.Id == userId && u.IsSuperAdmin, cancellationToken);
        if (isSuperAdmin) return true;

        var perm = await _db.UserSafePermissions
            .FirstOrDefaultAsync(p => p.UserId == userId && p.SafeId == safeId, cancellationToken);
        return perm != null && perm.CanAccess && predicate(perm);
    }

    public Task<bool> CanAccessSafeAsync(int userId, int safeId, CancellationToken cancellationToken = default)
        => CheckPermissionAsync(userId, safeId, p => true, cancellationToken);

    public Task<bool> CanViewBalanceAsync(int userId, int safeId, CancellationToken cancellationToken = default)
        => CheckPermissionAsync(userId, safeId, p => p.CanViewBalance, cancellationToken);

    public Task<bool> CanViewLedgerAsync(int userId, int safeId, CancellationToken cancellationToken = default)
        => CheckPermissionAsync(userId, safeId, p => p.CanViewLedger, cancellationToken);

    public Task<bool> CanCashInAsync(int userId, int safeId, CancellationToken cancellationToken = default)
        => CheckPermissionAsync(userId, safeId, p => p.CanCashIn, cancellationToken);

    public Task<bool> CanCashOutAsync(int userId, int safeId, CancellationToken cancellationToken = default)
        => CheckPermissionAsync(userId, safeId, p => p.CanCashOut, cancellationToken);

    public Task<bool> CanTransferFromAsync(int userId, int safeId, CancellationToken cancellationToken = default)
        => CheckPermissionAsync(userId, safeId, p => p.CanTransferFrom, cancellationToken);

    public Task<bool> CanReceiveTransferAsync(int userId, int safeId, CancellationToken cancellationToken = default)
        => CheckPermissionAsync(userId, safeId, p => p.CanReceiveTransfer, cancellationToken);

    private static object ToAuditState(UserSafePermission item) => new
    {
        item.SafeId,
        item.CanAccess,
        item.CanViewBalance,
        item.CanViewLedger,
        item.CanCashIn,
        item.CanCashOut,
        item.CanTransferFrom,
        item.CanReceiveTransfer
    };
}
