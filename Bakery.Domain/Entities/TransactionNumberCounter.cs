using Bakery.Domain.Interfaces;

namespace Bakery.Domain.Entities;

public sealed class TransactionNumberCounter : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string Prefix { get; set; } = string.Empty; // e.g. "DEP", "WDR", "REV"
    public int LastValue { get; set; }
}
