using Bakery.Application.DTOs.Inventory;
using Bakery.Domain.Enums;

namespace Bakery.Application.Interfaces;

public interface IInventoryService
{
    Task<(bool Succeeded, string? ErrorMessage)> AdjustStockAsync(InventoryAdjustmentRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryMovementDto>> GetMovementHistoryAsync(DateTime? from, DateTime? to, int? itemId, InventoryMovementType? type, CancellationToken cancellationToken = default);
    Task<int> StartStockCountAsync(StartStockCountRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockCountLineDto>> GetStockCountLinesAsync(int sessionId, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> CompleteStockCountAsync(CompleteStockCountRequest request, CancellationToken cancellationToken = default);
}
