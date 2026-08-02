using System;
using System.Threading.Tasks;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services;

public sealed class SafeSwitchService : ISafeSwitchService
{
    private readonly IUserSessionService _userSessionService;
    private readonly IUserSafePermissionService _userSafePermissionService;
    private readonly IInternalSafeContext _safeContext;

    public SafeSwitchService(
        IUserSessionService userSessionService,
        IUserSafePermissionService userSafePermissionService,
        ISafeContext safeContext)
    {
        _userSessionService = userSessionService;
        _userSafePermissionService = userSafePermissionService;
        _safeContext = safeContext.AsInternal();
    }

    public async Task SwitchSafeAsync(SafeDto safe)
    {
        if (safe == null) throw new ArgumentNullException(nameof(safe));

        int userId = ValidateCurrentUser();
        await ValidateSafeAccess(safe.Id, userId);
        SetCurrentSafe(safe);
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

    private async Task ValidateSafeAccess(int safeId, int userId)
    {
        var hasAccess = await _userSafePermissionService.CanAccessSafeAsync(userId, safeId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("The current user is not allowed to access this safe.");
        }
    }

    private void SetCurrentSafe(SafeDto safe)
    {
        _safeContext.ConfigureSafe(safe);
    }
}
