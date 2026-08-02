using Bakery.Domain.Enums;

namespace Bakery.Application.DTOs;

public sealed record OpenWorkingDayRequest(DateOnly BusinessDate, decimal OpeningCash, string? Notes);

public sealed record CloseWorkingDayRequest(
    decimal TransferredToMainSafe, 
    decimal CarryOverBalance,
    string? Notes, 
    bool AdminOverride = false, 
    string? OverrideReason = null,
    int? ExpectedWorkingDayId = null,
    Guid? OperationId = null);

public enum WorkingDayBlockerKind
{
    StockCount,
    ProductionOrder,
    SaleInvoice,
    PurchaseInvoice,
    TreasuryMovement,
    FinancialIntegrity,
    Validation
}

public sealed record WorkingDayBlockerDto(
    WorkingDayBlockerKind Kind,
    string Code,
    string Message,
    int? EntityId = null,
    string? ReferenceNumber = null,
    string? ActionLabel = null);

public sealed record WorkingDayCloseReadinessDto(
    WorkingDaySummaryDto? Summary,
    IReadOnlyList<WorkingDayBlockerDto> Blockers,
    string? ErrorMessage = null);

public sealed record WorkingDaySummaryDto(
    int WorkingDayId,
    DateOnly BusinessDate,
    WorkingDayStatus Status,
    decimal OpeningCash,
    decimal TotalSales,
    decimal TotalPurchases,
    decimal Expenses,
    decimal Wages,
    decimal SafeTransfers,
    decimal ExpectedCash,
    decimal? ActualCash,
    decimal? CashDifference,
    decimal TransferredToMainSafe = 0,
    decimal CarryOverBalance = 0,
    int ProductionCount = 0,
    decimal TotalIncome = 0,
    decimal DailySafeBalance = 0,
    int TransactionCount = 0,
    int InvoiceCount = 0,
    DateTime? ReopenedAt = null,
    string? ReopenedBy = null,
    string? ReopenReason = null,
    int ReopenCount = 0,
    DateTime? OpenedAt = null,
    string? OpenedBy = null,
    DateTime? LastClosedAt = null,
    string? LastClosedBy = null,
    decimal WasteCost = 0,
    int InventoryAdjustmentCount = 0,
    decimal ProductionEfficiency = 0);

public sealed record WorkingDayReopenEligibilityDto(
    WorkingDaySummaryDto? CurrentActiveDay,
    WorkingDaySummaryDto? LastClosedDay,
    bool CanReopen,
    string StatusMessage,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<WorkingDayReopenBlockerDto>? Blockers = null);

public enum WorkingDayReopenBlockerKind
{
    SaleInvoice,
    PurchaseInvoice,
    TreasuryTransaction,
    PartyPayment,
    ProductionOrder,
    InventoryAdjustment,
    StockCount,
    Waste,
    Expense,
    EmployeeWage,
    Attendance,
    EmployeeTransaction,
    Payroll,
    PartyLedger,
    LifecycleIntegrity
}

public enum WorkingDayReopenActionKind
{
    None,
    DeleteDraft,
    CancelInvoice,
    ReverseTransaction,
    CancelProduction
}

public sealed record WorkingDayReopenBlockerDto(
    string Code,
    WorkingDayReopenBlockerKind Kind,
    int EntityId,
    string TypeLabel,
    string RecordNumber,
    string Description,
    decimal? AmountOrQuantity,
    string? AmountOrQuantityLabel,
    string User,
    DateTime Timestamp,
    string Status,
    string BlockingReason,
    WorkingDayReopenActionKind ActionKind,
    string? ActionLabel,
    string? RequiredPermission,
    bool CanResolve,
    string EffectSummary,
    string? UnsupportedMessage = null);

public sealed record ResolveWorkingDayReopenBlockerRequest(
    string BlockerCode,
    string Reason,
    Guid CorrelationId);

public sealed record WorkingDayReopenBlockerResolutionResult(
    bool Succeeded,
    string? ErrorMessage,
    WorkingDayReopenEligibilityDto? Eligibility = null,
    bool WasAlreadyResolved = false);

public sealed record DashboardTrendPointDto(
    DateOnly BusinessDate,
    decimal Sales,
    decimal Production);

public sealed record WorkingDayResult(
    bool Succeeded,
    string? ErrorMessage,
    WorkingDaySummaryDto? Summary = null,
    IReadOnlyList<WorkingDayBlockerDto>? Blockers = null,
    bool WasAlreadyCompleted = false);

public sealed record ProductSalesDto(string ProductName, decimal Quantity, decimal TotalAmount);
public sealed record EmployeeSettlementSummaryDto(string EmployeeName, decimal Amount, string WageType);
public sealed record ExpenseSummaryDto(string Category, decimal Amount);

public sealed record ClosingReportDto(
    WorkingDaySummaryDto DaySummary,
    IReadOnlyList<ProductSalesDto> TopProducts,
    IReadOnlyList<EmployeeSettlementSummaryDto> Settlements,
    IReadOnlyList<ExpenseSummaryDto> Expenses,
    int SaleInvoiceCount,
    int PurchaseInvoiceCount
);
