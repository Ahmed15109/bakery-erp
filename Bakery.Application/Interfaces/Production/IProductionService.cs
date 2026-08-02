using Bakery.Application.DTOs;
using Bakery.Domain.Entities;

namespace Bakery.Application.Interfaces;

public interface IProductionService
{
    Task<IEnumerable<ProductionOrder>> GetAllProductionOrdersAsync();
    Task<ProductionOrder?> GetProductionOrderByIdAsync(int id);
    Task<ProductionOrder> CreateProductionOrderAsync(ProductionOrder order);
    Task UpdateProductionOrderAsync(ProductionOrder order);
    Task DeleteProductionOrderAsync(int id);
    Task PostProductionOrderAsync(int id);
    Task CancelProductionOrderAsync(int id);
    Task<ProductionSummaryDto> GetProductionSummaryAsync();
    Task<StockValidationResult> ValidateProductionStockAsync(int recipeId, decimal multiplier);
    Task<StockValidationResult> ValidateProductionItemsStockAsync(IEnumerable<ProductionConsumedItem> items);
}
