using Bakery.Application.DTOs;

namespace Bakery.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BranchDto>> GetActiveBranchesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserDto>> GetUsersForBranchAsync(int branchId, CancellationToken cancellationToken = default);
}
