using Bakery.Domain.Enums;
using Bakery.Domain.Interfaces;

namespace Bakery.Domain.Entities;

public sealed class Safe : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ArabicName { get; set; }
    public SafeType Type { get; set; } = SafeType.Normal;
    public bool IsSystem => Type != SafeType.Normal;
    public bool IsDefaultCashSafe => Type == SafeType.Daily;
    public bool IsActive { get; set; } = true;
    public ICollection<SafeMovement> Movements { get; set; } = [];
}

public sealed class SafeMovement : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int WorkingDayId { get; set; }
    public WorkingDay WorkingDay { get; set; } = null!;
    public int SafeId { get; set; }
    public Safe Safe { get; set; } = null!;
    public SafeMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public int? ReversalReferenceId { get; set; }
    public bool IsReversed { get; set; }
    public Guid? TransferId { get; set; }
    public string? Notes { get; set; }
    public string? IdempotencyKey { get; set; }

    // Manual transaction classification
    public CashMovementOrigin Origin { get; set; } = CashMovementOrigin.System;

    // Manual transaction details
    public string? TransactionNumber { get; set; }
    public ManualMovementReason? Reason { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? AttachmentPath { get; set; }

    // Audit balances
    public decimal? BalanceBefore { get; set; }
    public decimal? BalanceAfter { get; set; }

    // Audit user tracking
    public int? CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }

    // Reversal metadata (on the ORIGINAL transaction)
    public string? ReversedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    public string? ReverseReason { get; set; }
    public int? ReverseTransactionId { get; set; }

    // Reversal metadata (on the REVERSE transaction)
    public int? OriginalTransactionId { get; set; }
}
