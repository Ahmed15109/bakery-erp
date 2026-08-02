using Bakery.Domain.Enums;

namespace Bakery.Application.DTOs.Inventory;

public sealed record ItemDto(int Id, string Code, string Name, string? Barcode, ItemType Type, int BaseUnitId, string BaseUnit, decimal PurchasePrice, decimal SalePrice, decimal MinStockLevel, decimal ReorderLevel, bool IsActive, string? Notes, decimal CurrentStock = 0);

public sealed record SaveItemRequest(int? Id, string Code, string Name, string? Barcode, ItemType Type, int BaseUnitId, decimal PurchasePrice, decimal SalePrice, decimal MinStockLevel, decimal ReorderLevel, bool IsActive, string? Notes);

public sealed record UnitDto(int Id, string Name, string Symbol, bool IsActive);

public sealed record SaveUnitRequest(int? Id, string Name, string Symbol, bool IsActive);

public sealed record ItemUnitDto(int Id, int ItemId, int UnitId, string UnitName, decimal ConversionFactorToBaseUnit, bool IsDefaultUnit, bool IsDefaultPurchaseUnit, bool IsDefaultSaleUnit);

public sealed record SaveItemUnitRequest(int? Id, int ItemId, int UnitId, decimal ConversionFactorToBaseUnit, bool IsDefaultUnit, bool IsDefaultPurchaseUnit, bool IsDefaultSaleUnit);

public sealed record StockItemDto(int ItemId, string Code, string Name, string Unit, decimal Quantity, decimal UnitCost, decimal Value, decimal MinStockLevel, bool IsOutOfStock, bool IsBelowMinimum);

public sealed record InventoryAdjustmentRequest(int ItemId, int UnitId, decimal Quantity, bool IsIncrease, string Reason, bool AdminOverride = false);

public sealed record InventoryMovementDto(int Id, DateTime Date, string ItemCode, string ItemName, string Unit, InventoryMovementType Type, decimal Quantity, decimal UnitCost, decimal RunningBalance, string? Notes);

public sealed class StockCountLineDto
{
    public StockCountLineDto(int itemId, string itemCode, string itemName, int unitId, string unit, decimal systemQuantity, decimal physicalQuantity, decimal varianceQuantity)
    {
        ItemId = itemId;
        ItemCode = itemCode;
        ItemName = itemName;
        UnitId = unitId;
        Unit = unit;
        SystemQuantity = systemQuantity;
        PhysicalQuantity = physicalQuantity;
    }

    public int ItemId { get; init; }
    public string ItemCode { get; init; }
    public string ItemName { get; init; }
    public int UnitId { get; init; }
    public string Unit { get; init; }
    public decimal SystemQuantity { get; init; }
    public decimal PhysicalQuantity { get; set; }
    public decimal VarianceQuantity => PhysicalQuantity - SystemQuantity;
}

public sealed record StartStockCountRequest(string? Notes);

public sealed record CompleteStockCountRequest(int SessionId, IReadOnlyCollection<StockCountLineDto> Lines);
