using Bakery.Domain.Enums;

namespace Bakery.Application.DTOs;

public record ProductionSummaryDto(
    int TotalRecipes,
    int TodayOrdersCount,
    decimal TodayProductionCost,
    decimal TodayProducedValue
);

public record StockValidationResult(
    bool IsValid,
    List<MissingStockDto> MissingItems
);

public record MissingStockDto(
    int ItemId,
    string ItemName,
    decimal RequiredQuantity,
    decimal AvailableQuantity,
    string UnitName
);
