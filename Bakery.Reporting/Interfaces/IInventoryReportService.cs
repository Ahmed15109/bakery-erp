using Bakery.Application.DTOs.Inventory;
using Bakery.Domain.Enums;

namespace Bakery.Reporting.Interfaces;

public interface IInventoryReportService
{
    Task<IReadOnlyList<StockItemDto>> GetCurrentStockReportAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockItemDto>> GetLowStockReportAsync(CancellationToken cancellationToken = default);
    Task<decimal> GetInventoryValuationReportAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryMovementDto>> GetMovementHistoryReportAsync(DateTime? from, DateTime? to, int? itemId, InventoryMovementType? type, CancellationToken cancellationToken = default);
}
