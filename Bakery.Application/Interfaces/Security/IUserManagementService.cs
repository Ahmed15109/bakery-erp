using Bakery.Application.DTOs;

namespace Bakery.Application.Interfaces;

public interface IUserManagementService
{
    Task<IReadOnlyList<UserListItemDto>> SearchAsync(string? searchText, CancellationToken cancellationToken = default);
    Task<UserDetailsDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task<UserDetailsDto> CreateAsync(SaveUserRequest request, CancellationToken cancellationToken = default);
    Task<UserDetailsDto> UpdateAsync(SaveUserRequest request, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int userId, bool isActive, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<bool> CanDeleteAsync(int userId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int userId, CancellationToken cancellationToken = default);
}

public interface IRoleManagementService
{
    Task<IReadOnlyList<RoleListItemDto>> SearchAsync(string? searchText, CancellationToken cancellationToken = default);
    Task<RoleDetailsDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<RoleDetailsDto> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default);
    Task<RoleDetailsDto> UpdateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
