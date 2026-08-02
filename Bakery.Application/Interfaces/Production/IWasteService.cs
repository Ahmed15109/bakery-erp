using Bakery.Application.DTOs.Waste;
using Bakery.Application.DTOs.Inventory;

namespace Bakery.Application.Interfaces;

public interface IWasteService
{
    /// <summary>Returns all waste entries descending by date, with optional filters.</summary>
    Task<IReadOnlyList<WasteEntryDto>> GetEntriesAsync(
        string? itemNameFilter,
        DateTime? fromDate,
        DateTime? toDate,
        string? reasonFilter,
        int? itemIdFilter,
        CancellationToken ct = default);

    /// <summary>Summary statistics for today's waste (count, qty, cost).</summary>
    Task<WasteSummaryDto> GetTodaySummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves a new waste entry and creates the corresponding negative InventoryMovement atomically.
    /// Returns (true, null) on success or (false, errorMessage) if validation or saving fails.
    /// </summary>
    Task<(bool Succeeded, string? ErrorMessage)> SaveAsync(SaveWasteEntryRequest request, CancellationToken ct = default);
}
