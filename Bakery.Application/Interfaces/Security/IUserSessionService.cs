using Bakery.Application.DTOs;

namespace Bakery.Application.Interfaces;

public interface IUserSessionService : ICurrentUserService
{
    event EventHandler? AuthorizationChanged
    {
        add { }
        remove { }
    }
    AuthenticatedUserDto? CurrentUser { get; }
    void SignIn(AuthenticatedUserDto user);
    void SignOut();
    bool HasPermission(string permissionKey);
    void Refresh(AuthenticatedUserDto user) => SignIn(user);
    void InvalidateIfCurrentUser(int userId)
    {
        if (CurrentUser?.UserId == userId)
        {
            SignOut();
        }
    }
}
