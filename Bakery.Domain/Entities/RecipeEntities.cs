using Bakery.Domain.Interfaces;
using System.Collections.Generic;

namespace Bakery.Domain.Entities;

public sealed class Recipe : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int ProducedItemId { get; set; }
    public Item ProducedItem { get; set; } = null!;
    public decimal ProducedQuantity { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<RecipeItem> ConsumedItems { get; set; } = [];
}

public sealed class RecipeItem : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public int RawItemId { get; set; }
    public Item RawItem { get; set; } = null!;
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public decimal Quantity { get; set; }
}
