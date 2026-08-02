using Bakery.Domain.Entities;

namespace Bakery.Application.Interfaces;

public interface IDefaultCashSafeService
{
    Task<Safe> GetDefaultCashSafeAsync(CancellationToken cancellationToken = default);
}
