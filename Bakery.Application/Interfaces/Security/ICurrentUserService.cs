namespace Bakery.Application.Interfaces;

public interface ICurrentUserService
{
    int? UserId { get; }
    string Username { get; }
    string FullName { get; }
    IReadOnlyCollection<string> Permissions { get; }
    bool IsAuthenticated { get; }
    bool IsSuperAdmin { get; }
}
