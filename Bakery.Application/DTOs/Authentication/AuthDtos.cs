namespace Bakery.Application.DTOs;

public sealed record LoginRequest(string UserName, string Password, int? BranchId = null);

public sealed record AuthenticatedUserDto(
    int UserId,
    string Username,
    string FullName,
    IReadOnlyCollection<string> Permissions,
    bool IsSuperAdmin = false,
    string SecurityStamp = "",
    bool MustChangePassword = false,
    IReadOnlyCollection<string>? Roles = null)
{
    public string UserName => Username;
    public string DisplayName => FullName;
}

public sealed record AuthResult(
    bool Succeeded,
    string? ErrorMessage,
    AuthenticatedUserDto? User,
    IReadOnlyCollection<BranchDto>? AvailableBranches = null);

public sealed record UserDto(int Id, string Username, string FullName);
