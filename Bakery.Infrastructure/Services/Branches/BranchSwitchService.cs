using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class BranchSwitchService : IBranchSwitchService
{
    private readonly IUserSessionService _userSessionService;
    private readonly IBranchService _branchService;
    private readonly IInternalBranchContext _branchContext;
    private readonly IInternalSafeContext _safeContext;
    private readonly IUserSafePermissionService _userSafePermissionService;
    private readonly BakeryDbContext _dbContext;

    public BranchSwitchService(
        IUserSessionService userSessionService,
        IBranchService branchService,
        IBranchContext branchContext,
        ISafeContext safeContext,
        IUserSafePermissionService userSafePermissionService,
        BakeryDbContext dbContext)
    {
        _userSessionService = userSessionService;
        _branchService = branchService;
        _branchContext = branchContext.AsInternal();
        _safeContext = safeContext.AsInternal();
        _userSafePermissionService = userSafePermissionService;
        _dbContext = dbContext;
    }

    public async Task SwitchBranchAsync(BranchDto branch)
    {
        if (branch == null) throw new ArgumentNullException(nameof(branch));

        ValidatePermission();
        int userId = ValidateCurrentUser();
        await ValidateBranchOwnership(branch, userId);
        SetCurrentBranch(branch);
        await AutoSelectSafeForBranchAsync(branch.Id, userId);
    }

    private async Task AutoSelectSafeForBranchAsync(int branchId, int userId)
    {
        var safes = await _dbContext.Safes
            .IgnoreQueryFilters()
            .Where(s => s.BranchId == branchId && s.IsActive && !s.IsDeleted)
            .OrderByDescending(s => s.Type == Domain.Enums.SafeType.Daily)
            .ThenByDescending(s => s.Type == Domain.Enums.SafeType.Main)
            .ThenBy(s => s.Name)
            .ToListAsync();

        var allowedSafes = new List<Domain.Entities.Safe>();
        foreach (var s in safes)
        {
            if (await _userSafePermissionService.CanAccessSafeAsync(userId, s.Id))
            {
                allowedSafes.Add(s);
            }
        }

        if (allowedSafes.Count > 0)
        {
            var currentSafeId = _safeContext.CurrentSafeId;
            var currentValid = currentSafeId.HasValue
                ? allowedSafes.FirstOrDefault(s => s.Id == currentSafeId.Value)
                : null;
            var selectedSafe = currentValid ?? allowedSafes.First();

            _safeContext.ConfigureSafe(new SafeDto(selectedSafe.Id, selectedSafe.Name, selectedSafe.ArabicName, 0, selectedSafe.Type, null));
        }
        else
        {
            _safeContext.Clear();
        }
    }

    private void ValidatePermission()
    {
        if (!_userSessionService.HasPermission(PermissionKeys.BranchesSwitch))
        {
            throw new UnauthorizedAccessException("The current user is not allowed to switch branches.");
        }
    }

    private int ValidateCurrentUser()
    {
        var userId = _userSessionService.CurrentUser?.UserId;
        if (userId == null)
        {
            throw new InvalidOperationException("No user is currently logged in.");
        }
        return userId.Value;
    }

    private async Task ValidateBranchOwnership(BranchDto branch, int userId)
    {
        var userBranches = await _branchService.GetUserBranchesAsync(userId);
        if (userBranches == null || !userBranches.Any(b => b.Id == branch.Id))
        {
            throw new InvalidOperationException("Requested branch is not assigned to the current user.");
        }
    }

    private void SetCurrentBranch(BranchDto branch)
    {
        // Change the active branch using the configuration method
        _branchContext.ConfigureBranch(branch);
    }
}
