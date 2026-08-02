namespace Bakery.Application.DTOs.Waste;

public sealed record WasteEntryDto(
    int Id,
    DateTime CreatedAt,
    string ItemName,
    string UnitSymbol,
    decimal Quantity,
    decimal UnitCost,
    decimal WasteCost,
    string Reason,
    string? Notes,
    decimal StockAfter,
    string? CreatedBy);

public sealed record SaveWasteEntryRequest(
    int ItemId,
    int UnitId,
    decimal Quantity,
    decimal UnitCost,
    string Reason,
    string? Notes);

public sealed record WasteSummaryDto(
    int TodayCount,
    decimal TodayQuantity,
    decimal TodayCost);
