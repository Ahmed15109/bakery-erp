using Bakery.Domain.Enums;
using Bakery.Domain.Interfaces;

namespace Bakery.Domain.Entities;

public sealed class WorkingDay : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public DateOnly BusinessDate { get; set; }
    public WorkingDayStatus Status { get; set; } = WorkingDayStatus.Open;
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public string OpenedBy { get; set; } = string.Empty;
    public string? ClosedBy { get; set; }
    
    // Legacy fields - kept for compatibility
    public decimal OpeningCash { get; set; }
    public decimal? ClosingCash { get; set; }
    public decimal? ExpectedClosingCash { get; set; }
    public decimal? CashDifference { get; set; }
    
    // New Treasury Carry Over fields
    public decimal TransferredToMainSafe { get; set; }
    public decimal CarryOverBalance { get; set; }
    
    public string? Notes { get; set; }

    // Snapshot Totals (Immutable after close)
    public decimal TotalSales { get; set; }
    public decimal TotalPurchases { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal TotalWages { get; set; }
    public decimal TotalSafeMovements { get; set; }
    public decimal TotalInventoryAdjustments { get; set; }
    public int InvoiceCount { get; set; }
}
