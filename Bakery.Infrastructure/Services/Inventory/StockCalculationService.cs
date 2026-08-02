using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class StockCalculationService : IStockCalculationService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IPermissionService _permissionService;
    private readonly IItemUnitConversionService _unitConversionService;

    public StockCalculationService(
        BakeryDbContext dbContext,
        IPermissionService permissionService,
        IItemUnitConversionService unitConversionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
        _unitConversionService = unitConversionService;
    }

    public async Task<decimal> GetCurrentStockAsync(int itemId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.InventoryView,
            PermissionKeys.ReportsInventory,
            PermissionKeys.ProductsView,
            PermissionKeys.SalesCreate,
            PermissionKeys.ProductionView,
            PermissionKeys.ProductionCreate,
            PermissionKeys.ProductionEdit,
            PermissionKeys.ProductionWaste,
            PermissionKeys.InventoryStockAdjustments,
            PermissionKeys.InventoryCount);
        var balances = await GetCurrentStockAsync([itemId], cancellationToken);
        return balances.GetValueOrDefault(itemId);
    }

    public async Task<IReadOnlyDictionary<int, decimal>> GetCurrentStockAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default)
    {
        EnsureStockReadPermission();
        if (itemIds.Count == 0) return new Dictionary<int, decimal>();

        var ids = itemIds.Distinct().ToArray();
        var movements = await _dbContext.InventoryMovements
            .Where(movement => ids.Contains(movement.ItemId))
            .Select(movement => new { movement.ItemId, movement.UnitId, movement.Quantity })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var conversions = await _unitConversionService.GetConversionsAsync(
            movements.Select(movement => new ItemUnitKey(movement.ItemId, movement.UnitId)),
            cancellationToken);
        var balances = ids.ToDictionary(id => id, _ => 0m);
        foreach (var movement in movements)
        {
            var conversion = conversions[new ItemUnitKey(movement.ItemId, movement.UnitId)];
            balances[movement.ItemId] += conversion.ToBaseQuantity(movement.Quantity);
        }
        return balances;
    }

    public async Task<IReadOnlyList<StockItemDto>> GetCurrentStockAsync(CancellationToken cancellationToken = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.InventoryView) && !_permissionService.HasPermission(PermissionKeys.ReportsInventory))
        {
            throw new UnauthorizedAccessException("You do not have permission to view stock.");
        }

        var items = await _dbContext.Items.Include(item => item.BaseUnit).AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var balances = await GetCurrentStockAsync(items.Select(item => item.Id).ToArray(), cancellationToken);

        var canViewCost = _permissionService.HasPermission(PermissionKeys.ProductsViewCost);
        return items.Select(item =>
        {
            var quantity = balances.GetValueOrDefault(item.Id);
            var unitCost = canViewCost ? item.PurchasePrice : 0m;
            return new StockItemDto(item.Id, item.Code, item.Name, item.BaseUnit.Symbol, quantity, unitCost, quantity * unitCost, item.MinStockLevel, quantity <= 0, quantity < item.MinStockLevel);
        }).ToList();
    }

    public async Task<IReadOnlyList<StockItemDto>> GetLowStockItemsAsync(CancellationToken cancellationToken = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.InventoryView) && 
            !_permissionService.HasPermission(PermissionKeys.ReportsInventory))
        {
            throw new UnauthorizedAccessException("You do not have permission to view low stock items.");
        }

        return (await GetCurrentStockAsync(cancellationToken)).Where(item => item.IsOutOfStock || item.IsBelowMinimum).ToList();
    }

    public async Task<decimal> GetStockValuationAsync(CancellationToken cancellationToken = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.InventoryView) && !_permissionService.HasPermission(PermissionKeys.ReportsInventory))
        {
            throw new UnauthorizedAccessException("You do not have permission to view stock valuation.");
        }
        _permissionService.EnsurePermission(PermissionKeys.ProductsViewCost);

        return (await GetCurrentStockAsync(cancellationToken)).Sum(item => item.Value);
    }

    public async Task<bool> HasAvailableStockAsync(int itemId, decimal quantity, CancellationToken cancellationToken = default)
    {
        return await GetCurrentStockAsync(itemId, cancellationToken) >= quantity;
    }

    private void EnsureStockReadPermission()
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.InventoryView,
            PermissionKeys.ReportsInventory,
            PermissionKeys.ProductsView,
            PermissionKeys.SalesCreate,
            PermissionKeys.ProductionView,
            PermissionKeys.ProductionCreate,
            PermissionKeys.ProductionEdit,
            PermissionKeys.ProductionWaste,
            PermissionKeys.InventoryStockAdjustments,
            PermissionKeys.InventoryCount);
    }
}
