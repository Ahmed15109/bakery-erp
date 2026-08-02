using Bakery.Application.DTOs.Inventory;

namespace Bakery.Application.Interfaces;

public interface IStockCalculationService
{
    Task<decimal> GetCurrentStockAsync(int itemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, decimal>> GetCurrentStockAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockItemDto>> GetCurrentStockAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockItemDto>> GetLowStockItemsAsync(CancellationToken cancellationToken = default);
    Task<decimal> GetStockValuationAsync(CancellationToken cancellationToken = default);
    Task<bool> HasAvailableStockAsync(int itemId, decimal quantity, CancellationToken cancellationToken = default);
}
