using Bakery.Domain.Enums;
using Bakery.Domain.Interfaces;
using System;
using System.Collections.Generic;

namespace Bakery.Domain.Entities;

public sealed class ProductionOrder : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string ProductionNumber { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int WorkingDayId { get; set; }
    public WorkingDay WorkingDay { get; set; } = null!;
    public int? RecipeId { get; set; }
    public Recipe? Recipe { get; set; }
    public ProductionStatus Status { get; set; } = ProductionStatus.Draft;
    public string? BatchNumber { get; set; }
    public string? RecipeSnapshotJson { get; set; }
    public string? Notes { get; set; }
    public ICollection<ProductionConsumedItem> ConsumedItems { get; set; } = [];
    public ICollection<ProductionProducedItem> ProducedItems { get; set; } = [];
    public ICollection<ProductionOrderEmployee> Employees { get; set; } = [];
}

public sealed class ProductionOrderEmployee : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int ProductionOrderId { get; set; }
    public ProductionOrder ProductionOrder { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public decimal ContributionPercentage { get; set; }
    
    // Snapshots
    public WageType WageTypeSnapshot { get; set; }
    public decimal WageAmountSnapshot { get; set; }
    
    public decimal CalculatedWage { get; set; }
    public string? Notes { get; set; }
}

public sealed class ProductionConsumedItem : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int ProductionOrderId { get; set; }
    public ProductionOrder ProductionOrder { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public sealed class ProductionProducedItem : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int ProductionOrderId { get; set; }
    public ProductionOrder ProductionOrder { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public decimal ExpectedProducedQty { get; set; }
    public decimal ActualProducedQty { get; set; }
    public decimal VarianceQty { get; set; }
    public string? VarianceReason { get; set; }
    public decimal UnitCost { get; set; }
}
