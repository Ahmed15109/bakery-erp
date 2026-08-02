using Bakery.Domain.Entities;
using Bakery.Domain.Enums;

namespace Bakery.Application.Interfaces;

public interface ISystemSafeService
{
    Task EnsureSystemSafesAsync(CancellationToken cancellationToken = default);
    Task<Safe> GetDailySafeAsync(CancellationToken cancellationToken = default);
    Task<Safe> GetMainSafeAsync(CancellationToken cancellationToken = default);
    Task<Safe> GetPrivateSafeAsync(CancellationToken cancellationToken = default);
    Task<Safe?> GetSafeByTypeAsync(SafeType type, CancellationToken cancellationToken = default);
}
