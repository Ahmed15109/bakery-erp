using System.Threading;
using System.Threading.Tasks;
using Bakery.Application.DTOs;

namespace Bakery.Application.Interfaces;

public interface IUserSafePermissionService
{
    Task<GetUserSafePermissionsResponse> GetUserPermissionsAsync(int userId, CancellationToken cancellationToken = default);
    Task UpdateUserPermissionsAsync(UpdateUserSafePermissionsRequest request, CancellationToken cancellationToken = default);
    Task<bool> CanAccessSafeAsync(int userId, int safeId, CancellationToken cancellationToken = default);
    Task<bool> CanViewBalanceAsync(int userId, int safeId, CancellationToken cancellationToken = default);
    Task<bool> CanViewLedgerAsync(int userId, int safeId, CancellationToken cancellationToken = default);
    Task<bool> CanCashInAsync(int userId, int safeId, CancellationToken cancellationToken = default);
    Task<bool> CanCashOutAsync(int userId, int safeId, CancellationToken cancellationToken = default);
    Task<bool> CanTransferFromAsync(int userId, int safeId, CancellationToken cancellationToken = default);
    Task<bool> CanReceiveTransferAsync(int userId, int safeId, CancellationToken cancellationToken = default);
}
