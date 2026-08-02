using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Enums;
using Bakery.Reporting.Interfaces;

namespace Bakery.Reporting.Services;

public sealed class InventoryReportService : IInventoryReportService
{
    private readonly IStockCalculationService _stockCalculationService;
    private readonly IInventoryService _inventoryService;
    private readonly IPermissionService _permissionService;

    public InventoryReportService(IStockCalculationService stockCalculationService, IInventoryService inventoryService, IPermissionService permissionService)
    {
        _stockCalculationService = stockCalculationService;
        _inventoryService = inventoryService;
        _permissionService = permissionService;
    }

    public async Task<IReadOnlyList<StockItemDto>> GetCurrentStockReportAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsInventory);
        var rows = await _stockCalculationService.GetCurrentStockAsync(cancellationToken);
        return _permissionService.HasPermission(PermissionKeys.ProductsViewCost)
            ? rows
            : rows.Select(row => row with { UnitCost = 0, Value = 0 }).ToArray();
    }

    public Task<IReadOnlyList<StockItemDto>> GetLowStockReportAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsInventory);
        return _stockCalculationService.GetLowStockItemsAsync(cancellationToken);
    }

    public Task<decimal> GetInventoryValuationReportAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsInventory);
        _permissionService.EnsurePermission(PermissionKeys.ProductsViewCost);
        return _stockCalculationService.GetStockValuationAsync(cancellationToken);
    }

    public Task<IReadOnlyList<InventoryMovementDto>> GetMovementHistoryReportAsync(DateTime? from, DateTime? to, int? itemId, InventoryMovementType? type, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsInventory);
        return _inventoryService.GetMovementHistoryAsync(from, to, itemId, type, cancellationToken);
    }
}
