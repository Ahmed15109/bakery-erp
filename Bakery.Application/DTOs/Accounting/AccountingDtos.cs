using Bakery.Domain.Enums;
using System.Globalization;

namespace Bakery.Application.DTOs.Accounting;

public sealed record PartyDto(int Id, string Name, PartyType Type, string? Phone, string? Address, string? NationalId, string? Notes, bool IsActive, decimal Balance);
public sealed record SavePartyRequest(int? Id, string Name, PartyType Type, string? Phone, string? Address, string? NationalId, string? Notes, bool IsActive);
public sealed record PartyStatsDto(int Customers, int Suppliers, int Employees, decimal TotalBalance);
public sealed record PartyStatementLineDto(DateTime Date, string Description, decimal Increase, decimal Decrease, decimal Remaining, decimal RunningBalance, string? ReferenceType, int? ReferenceId);
public sealed record PartySummaryDto(string Name, PartyType Type, decimal TotalIncrease, decimal TotalDecrease, decimal CurrentBalance);

public sealed record InvoiceLineRequest(int ItemId, int UnitId, decimal Quantity, decimal UnitPrice);
public sealed record SaveSaleInvoiceRequest(int? Id, int CustomerId, PaymentType PaymentType, decimal PaidAmount, string? Notes, IReadOnlyCollection<InvoiceLineRequest> Lines, int? SafeId);
public sealed record SavePurchaseInvoiceRequest(int? Id, int SupplierId, PaymentType PaymentType, decimal PaidAmount, string? Notes, IReadOnlyCollection<InvoiceLineRequest> Lines, int? SafeId);
public sealed record InvoiceDto(int Id, string InvoiceNumber, DateTime Date, string PartyName, PaymentType PaymentType, InvoiceStatus Status, decimal TotalAmount, decimal PaidAmount, decimal RemainingAmount);
public sealed record InvoicePrintDto(
    string InvoiceNumber,
    DateTime Date,
    string PartyName,
    IReadOnlyCollection<InvoicePrintLineDto> Lines,
    decimal Total,
    decimal Paid,
    decimal Remaining,
    string Layout,
    string BusinessName = "Bakery ERP",
    string? BranchName = null,
    string? Cashier = null,
    string DocumentType = "Invoice",
    PaymentType PaymentType = PaymentType.Cash,
    decimal Discount = 0m,
    decimal Tax = 0m,
    string? Footer = null);
public sealed record InvoicePrintLineDto(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    string? UnitName = null);
public sealed record ReceiptRenderContext(
    int? WorkingDayId,
    DateOnly? BusinessDate,
    string PrintedBy,
    DateTime PrintedAt);
public sealed record SalesByItemDto(
    int ItemId,
    string ItemCode,
    string ItemName,
    string BaseUnit,
    decimal Quantity,
    decimal GrossSales,
    decimal Discounts,
    decimal ReturnQuantity,
    decimal Returns,
    decimal NetQuantity,
    decimal NetSales);
public sealed record SafeMovementDto(
    int Id,
    int TreasuryId,
    DateTime Date,
    string SafeName,
    string Description,
    SafeMovementType Type,
    decimal Amount,
    decimal RunningBalance,
    string? ReferenceType,
    int? ReferenceId,
    string? Notes,
    Guid? TransferId,
    string? CounterpartSafeName,
    
    CashMovementOrigin Origin,
    string? TransactionNumber,
    ManualMovementReason? Reason,
    string? ReasonText,
    bool IsReversed,
    int? OriginalTransactionId,
    string? CreatedBy,
    string? ReversedBy,
    DateTime? ReversedAt,
    string? ReverseReason,
    decimal? BalanceBefore,
    decimal? BalanceAfter,
    string? ReversedByTransactionNumber = null,
    string? OriginalTransactionNumber = null
)
{
    public decimal? Incoming => Amount > 0 ? Amount : null;
    public decimal? Outgoing => Amount < 0 ? Math.Abs(Amount) : null;
    public string DisplayTransactionNumber => string.IsNullOrWhiteSpace(TransactionNumber)
        ? Id.ToString(CultureInfo.InvariantCulture)
        : TransactionNumber;
    public string StatusText => IsReversed ? "ملغاة" 
        : Origin == CashMovementOrigin.Reverse ? "حركة عكسية" 
        : "نشطة";
}
public sealed record SafeDto(
    int Id,
    string Name,
    string? ArabicName,
    decimal Balance,
    SafeType Type = SafeType.Normal,
    string? BranchName = null)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(ArabicName) ? ArabicName : Name;
    public string TypeDisplayName => Type switch
    {
        SafeType.Main => "خزينة رئيسية",
        SafeType.Private => "خزينة خاصة",
        SafeType.Daily => "خزينة يومية",
        _ => "خزينة عادية"
    };
    public string Subtitle => string.IsNullOrWhiteSpace(BranchName)
        ? TypeDisplayName
        : $"{BranchName} • {TypeDisplayName}";
}

public sealed record TreasurySnapshotDto(
    int TreasuryId,
    string TreasuryName,
    SafeType TreasuryType,
    string BranchName,
    int? WorkingDayId,
    DateOnly? BusinessDate,
    WorkingDayStatus? WorkingDayStatus,
    decimal CurrentBalance,
    decimal TodayReceipts,
    decimal TodayPayments,
    decimal OpeningBalance,
    decimal TodaySales,
    decimal ExpectedCash,
    decimal CarriedBalance,
    bool CanViewBalance,
    bool CanViewLedger,
    bool CanDeposit,
    bool CanWithdraw,
    bool CanTransfer);

public sealed record TreasuryReportDto(
    int TreasuryId,
    TreasurySnapshotDto Summary,
    IReadOnlyList<SafeMovementDto> Movements,
    DateTime? StartDate,
    DateTime? EndDate,
    SafeMovementType? MovementType,
    string? Search);

public sealed record SafeManagementDto(
    int Id,
    string Name,
    string? ArabicName,
    SafeType Type,
    bool IsActive,
    decimal Balance
)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(ArabicName) ? ArabicName : Name;
    public bool IsSystem => Type != SafeType.Normal;
    public bool IsDefaultCashSafe => Type == SafeType.Daily;
    public string TypeDisplayName => Type switch
    {
        SafeType.Main => "ثابتة - رئيسية",
        SafeType.Private => "ثابتة - خاصة",
        SafeType.Daily => "ثابتة - اليوم",
        SafeType.Normal => "عادية",
        _ => "عادية"
    };
}

public sealed record CreateSafeRequest(string ArabicName);
public sealed record UpdateSafeRequest(int Id, string ArabicName, bool IsActive);

public sealed record ManualCashTransactionRequest(
    int SafeId,
    decimal Amount,
    ManualMovementReason Reason,
    string Description,
    string? ReferenceNumber,
    string? AttachmentPath,
    string? IdempotencyKey = null
);

public sealed record ReverseTransactionRequest(
    int OriginalTransactionId,
    string ReverseReason
);
