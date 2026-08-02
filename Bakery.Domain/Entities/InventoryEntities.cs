using Bakery.Domain.Enums;
using Bakery.Domain.Interfaces;
using System;
using System.Collections.Generic;

namespace Bakery.Domain.Entities;

public sealed class Item : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public ItemType Type { get; set; }
    public int BaseUnitId { get; set; }
    public Unit BaseUnit { get; set; } = null!;
    public decimal PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }
    public decimal MinStockLevel { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public ICollection<ItemUnit> ItemUnits { get; set; } = [];
    public ICollection<InventoryMovement> InventoryMovements { get; set; } = [];
}

public sealed class Unit : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<Item> BaseUnitItems { get; set; } = [];
    public ICollection<ItemUnit> ItemUnits { get; set; } = [];
}

public sealed class ItemUnit : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public decimal ConversionFactorToBaseUnit { get; set; } = 1;
    public bool IsDefaultPurchaseUnit { get; set; }
    public bool IsDefaultSaleUnit { get; set; }
    public bool IsDefaultUnit { get; set; }
}

public sealed class InventoryMovement : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int WorkingDayId { get; set; }
    public WorkingDay WorkingDay { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public InventoryMovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string? Notes { get; set; }
    public int? ReversalReferenceId { get; set; }
    public bool IsReversed { get; set; }
}

public sealed class StockCountSession : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string StartedBy { get; set; } = string.Empty;
    public string? CompletedBy { get; set; }
    public bool IsCompleted { get; set; }
    public string? Notes { get; set; }
    public ICollection<StockCountLine> Lines { get; set; } = [];
}

public sealed class StockCountLine : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int StockCountSessionId { get; set; }
    public StockCountSession StockCountSession { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public decimal SystemQuantity { get; set; }
    public decimal PhysicalQuantity { get; set; }
    public decimal VarianceQuantity { get; set; }
}
