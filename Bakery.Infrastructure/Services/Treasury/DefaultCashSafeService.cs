using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;

namespace Bakery.Infrastructure.Services;

public sealed class DefaultCashSafeService : IDefaultCashSafeService
{
    private readonly ISystemSafeService _systemSafeService;

    public DefaultCashSafeService(ISystemSafeService systemSafeService)
    {
        _systemSafeService = systemSafeService;
    }

    public async Task<Safe> GetDefaultCashSafeAsync(CancellationToken cancellationToken = default)
    {
        // Delegate to system safe service to retrieve/ensure the Daily Cash Safe
        return await _systemSafeService.GetDailySafeAsync(cancellationToken);
    }
}
