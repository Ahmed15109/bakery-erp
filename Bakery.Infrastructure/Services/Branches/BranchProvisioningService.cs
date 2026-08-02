using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bakery.Infrastructure.Services;

public sealed class BranchProvisioningService : IBranchProvisioningService
{
    private readonly BakeryDbContext _dbContext;
    private readonly ISystemSafeService _systemSafeService;
    private readonly IPermissionService _permissionService;

    public BranchProvisioningService(
        BakeryDbContext dbContext,
        ISystemSafeService systemSafeService,
        IPermissionService permissionService)
    {
        _dbContext = dbContext;
        _systemSafeService = systemSafeService;
        _permissionService = permissionService;
    }

    public async Task ProvisionBranchAsync(int branchId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.SettingsBranchManagement);
        // 1. Provision default system safes for the branch
        if (_systemSafeService is SystemSafeService systemSafeService)
        {
            await systemSafeService.EnsureSystemSafesCoreAsync(cancellationToken);
        }
        else
        {
            await _systemSafeService.EnsureSystemSafesAsync(cancellationToken);
        }

        // 2. Provision default settings for the branch
        var defaultSettings = new[]
        {
            ("UiCulture", "ar-EG", "Default UI culture"),
            ("Inventory.AllowNegativeStock", "false", "Prevent negative stock sales"),
            ("Treasury.AllowNegativeSafeBalance", "false", "Prevent negative safe balances"),
            ("Accounting.AllowNegativePartyBalance", "false", "Prevent negative customer/supplier balances")
        };

        foreach (var (key, val, desc) in defaultSettings)
        {
            var exists = await _dbContext.AppSettings
                .IgnoreQueryFilters()
                .AnyAsync(s => s.BranchId == branchId && s.Key == key, cancellationToken);

            if (!exists)
            {
                _dbContext.AppSettings.Add(new AppSetting
                {
                    BranchId = branchId,
                    Key = key,
                    Value = val,
                    Description = desc
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
