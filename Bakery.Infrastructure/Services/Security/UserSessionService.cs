using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services;

public sealed class UserSessionService : IUserSessionService
{
    private HashSet<string> _permissions = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? AuthorizationChanged;
    public AuthenticatedUserDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;
    public int? UserId => CurrentUser?.UserId;
    public string Username => CurrentUser?.Username ?? string.Empty;
    public string FullName => CurrentUser?.FullName ?? string.Empty;
    public IReadOnlyCollection<string> Permissions => _permissions;

    public bool IsSuperAdmin => CurrentUser?.IsSuperAdmin == true;

    public void SignIn(AuthenticatedUserDto user)
    {
        CurrentUser = user;
        _permissions = user.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        AuthorizationChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        CurrentUser = null;
        _permissions.Clear();
        AuthorizationChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool HasPermission(string permissionKey)
    {
        if (IsSuperAdmin) return true;
        return _permissions.Contains(permissionKey);
    }

    public void Refresh(AuthenticatedUserDto user)
    {
        SignIn(user);
    }

    public void InvalidateIfCurrentUser(int userId)
    {
        if (CurrentUser?.UserId == userId)
        {
            SignOut();
        }
    }
}
