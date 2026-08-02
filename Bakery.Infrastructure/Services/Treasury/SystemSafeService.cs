using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bakery.Infrastructure.Services;

public sealed class SystemSafeService : ISystemSafeService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IPermissionService _permissionService;

    public SystemSafeService(
        BakeryDbContext dbContext,
        IPermissionService permissionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
    }

    public Task EnsureSystemSafesAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.TreasuryManageSafes,
            PermissionKeys.SettingsBranchManagement);
        return EnsureSystemSafesCoreAsync(cancellationToken);
    }

    internal async Task EnsureSystemSafesCoreAsync(CancellationToken cancellationToken = default)
    {
        var branches = await _dbContext.Branches
            .IgnoreQueryFilters()
            .Where(b => b.IsActive && !b.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var branch in branches)
        {
            // 1. Main Safe
            await EnsureSafeExistsAsync(branch.Id, "MAIN_SAFE", "Main Safe", "الخزنة الرئيسية", SafeType.Main, cancellationToken);
            
            // 2. Private Safe
            await EnsureSafeExistsAsync(branch.Id, "PRIVATE_SAFE", "Private Safe", "الخزنة الخاصة", SafeType.Private, cancellationToken);
            
            // 3. Daily Cash Safe
            await EnsureSafeExistsAsync(branch.Id, "DAILY_CASH_SAFE", "Daily Cash Safe", "خزنة رصيد اليوم", SafeType.Daily, cancellationToken);
        }
    }

    public Task<Safe> GetDailySafeAsync(CancellationToken cancellationToken = default)
        => GetRequiredSafeByTypeAsync(SafeType.Daily, cancellationToken);

    public Task<Safe> GetMainSafeAsync(CancellationToken cancellationToken = default)
        => GetRequiredSafeByTypeAsync(SafeType.Main, cancellationToken);

    public Task<Safe> GetPrivateSafeAsync(CancellationToken cancellationToken = default)
        => GetRequiredSafeByTypeAsync(SafeType.Private, cancellationToken);

    public async Task<Safe?> GetSafeByTypeAsync(SafeType type, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Safes
            .FirstOrDefaultAsync(s => s.Type == type, cancellationToken);
    }

    private async Task<Safe> GetRequiredSafeByTypeAsync(SafeType type, CancellationToken cancellationToken)
    {
        var safe = await _dbContext.Safes
            .FirstOrDefaultAsync(s => s.Type == type && s.IsActive, cancellationToken);

        if (safe is null)
        {
            // Operational services are allowed to recover deterministic system safes.
            // The general-purpose provisioning entry point remains permission protected.
            await EnsureSystemSafesCoreAsync(cancellationToken);
            safe = await _dbContext.Safes
                .FirstOrDefaultAsync(s => s.Type == type && s.IsActive, cancellationToken);
        }

        return safe ?? throw new InvalidOperationException($"System safe of type {type} is not configured or is inactive.");
    }

    private async Task<Safe> EnsureSafeExistsAsync(int branchId, string code, string name, string arabicName, SafeType type, CancellationToken ct)
    {
        var safe = await _dbContext.Safes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entity => entity.BranchId == branchId && (entity.Type == type || entity.Code == code), ct);

        if (safe is null)
        {
            safe = new Safe
            {
                BranchId = branchId,
                Code = code,
                Name = name,
                ArabicName = arabicName,
                Type = type,
                IsActive = true
            };

            _dbContext.Safes.Add(safe);
            await _dbContext.SaveChangesAsync(ct);
            return safe;
        }

        var changed = false;

        if (safe.Type != type)
        {
            safe.Type = type;
            changed = true;
        }

        if (safe.Code != code)
        {
            safe.Code = code;
            changed = true;
        }

        if (safe.Name != name)
        {
            safe.Name = name;
            changed = true;
        }

        if (safe.ArabicName != arabicName)
        {
            safe.ArabicName = arabicName;
            changed = true;
        }

        if (!safe.IsActive)
        {
            safe.IsActive = true;
            changed = true;
        }

        if (safe.IsDeleted)
        {
            safe.IsDeleted = false;
            changed = true;
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(ct);
        }

        return safe;
    }
}
