namespace Bakery.Application.DTOs;

public sealed record PermissionDto(
    int Id,
    string Key,
    string DisplayName,
    string Category);

public sealed record UserListItemDto(
    int Id,
    string Username,
    string FullName,
    bool IsActive,
    int PermissionCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyCollection<string>? RoleNames = null)
{
    public string RolesDisplay => RoleNames is { Count: > 0 }
        ? string.Join("، ", RoleNames)
        : "صلاحيات فردية";
}

public sealed record UserDetailsDto(
    int Id,
    string Username,
    string FullName,
    bool IsActive,
    IReadOnlyCollection<string> PermissionKeys,
    IReadOnlyCollection<int> BranchIds,
    IReadOnlyCollection<int>? RoleIds = null,
    IReadOnlyCollection<UserSafePermissionDto>? SafePermissions = null,
    string? RowVersion = null,
    bool MustChangePassword = false);

public sealed record SaveUserRequest(
    int? Id,
    string Username,
    string FullName,
    string? Password,
    bool IsActive,
    IReadOnlyCollection<string>? PermissionKeys,
    IReadOnlyCollection<int>? BranchIds,
    IReadOnlyCollection<int>? RoleIds = null,
    IReadOnlyCollection<UserSafePermissionDto>? SafePermissions = null,
    string? RowVersion = null);

public sealed record ResetPasswordRequest(
    int UserId,
    string NewPassword);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);

public sealed record FirstRunAdminRequest(
    string Username,
    string FullName,
    string Password,
    string ConfirmPassword);

public sealed record FirstRunSetupResult(
    bool Succeeded,
    string? ErrorMessage = null,
    int? UserId = null);

public sealed record RoleListItemDto(
    int Id,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsProtected,
    int UserCount,
    int PermissionCount);

public sealed record RoleDetailsDto(
    int Id,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsProtected,
    IReadOnlyCollection<string> PermissionKeys,
    string RowVersion);

public sealed record SaveRoleRequest(
    int? Id,
    string Name,
    string? Description,
    IReadOnlyCollection<string> PermissionKeys,
    string? RowVersion = null);
