using Bakery.Application.DTOs.Waste;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services;

public sealed class WasteService : IWasteService
{
    private readonly BakeryDbContext _db;
    private readonly IWorkingDayService _days;
    private readonly IStockCalculationService _stock;
    private readonly IAuditService _audit;
    private readonly ILogger<WasteService> _logger;
    private readonly IPermissionService _permissionService;
    private readonly IItemUnitConversionService _unitConversionService;
    private readonly IStockMutationLock _stockMutationLock;
    private readonly IBusinessDateService _businessDateService;

    public WasteService(
        BakeryDbContext db,
        IWorkingDayService days,
        IStockCalculationService stock,
        IAuditService audit,
        ILogger<WasteService> logger,
        IPermissionService permissionService,
        IItemUnitConversionService unitConversionService,
        IStockMutationLock stockMutationLock,
        IBusinessDateService businessDateService)
    {
        _db = db;
        _days = days;
        _stock = stock;
        _audit = audit;
        _logger = logger;
        _permissionService = permissionService;
        _unitConversionService = unitConversionService;
        _stockMutationLock = stockMutationLock;
        _businessDateService = businessDateService;
    }

    public async Task<IReadOnlyList<WasteEntryDto>> GetEntriesAsync(
        string? itemNameFilter,
        DateTime? fromDate,
        DateTime? toDate,
        string? reasonFilter,
        int? itemIdFilter,
        CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionWaste);
        var query = _db.WasteEntries
            .AsNoTracking()
            .Include(w => w.Item).ThenInclude(item => item.BaseUnit)
            .Include(w => w.Unit)
            .AsQueryable();
        
        if (!string.IsNullOrWhiteSpace(itemNameFilter))
            query = query.Where(w => w.Item.Name.Contains(itemNameFilter));

        if (fromDate.HasValue)
            query = query.Where(w => w.CreatedAt >= fromDate.Value.Date);

        if (toDate.HasValue)
            query = query.Where(w => w.CreatedAt < toDate.Value.Date.AddDays(1));

        if (!string.IsNullOrWhiteSpace(reasonFilter))
            query = query.Where(w => w.Reason == reasonFilter);

        if (itemIdFilter.HasValue)
            query = query.Where(w => w.ItemId == itemIdFilter.Value);

        var entries = await query
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);

        // For each entry, compute stock after = sum of all movements up to CreatedAt
        // We load this in one batch to avoid N+1
        var itemIds = entries.Select(e => e.ItemId).Distinct().ToList();
        var movements = await _db.InventoryMovements
            .AsNoTracking()
            .Where(m => itemIds.Contains(m.ItemId))
            .ToListAsync(ct);
        var movementConversions = await _unitConversionService.GetConversionsAsync(
            movements.Select(movement => new ItemUnitKey(movement.ItemId, movement.UnitId)), ct);
        var entryConversions = await _unitConversionService.GetConversionsAsync(
            entries.Select(entry => new ItemUnitKey(entry.ItemId, entry.UnitId)), ct);

        return entries.Select(w =>
        {
            var entryConversion = entryConversions[new ItemUnitKey(w.ItemId, w.UnitId)];
            // Stock after = sum of all movements for this item up to and including this entry's time
            var stockAfter = movements
                .Where(m => m.ItemId == w.ItemId &&
                    (m.CreatedAt <= w.CreatedAt ||
                     (m.ReferenceType == "Waste" && m.ReferenceId == w.Id)))
                .Sum(m => movementConversions[new ItemUnitKey(m.ItemId, m.UnitId)].ToBaseQuantity(m.Quantity));

            return new WasteEntryDto(
                w.Id,
                w.CreatedAt,
                w.Item.Name,
                w.Item.BaseUnit.Symbol,
                entryConversion.ToBaseQuantity(w.Quantity),
                entryConversion.ToBaseUnitCost(w.UnitCost),
                w.WasteCost,
                w.Reason,
                w.Notes,
                stockAfter,
                w.CreatedBy);
        }).ToList();
    }

    public async Task<WasteSummaryDto> GetTodaySummaryAsync(CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionWaste);
        var businessDay = await _businessDateService.GetCurrentAsync(ct);
        if (businessDay is null) return new WasteSummaryDto(0, 0, 0);

        var todayEntries = await _db.WasteEntries
            .AsNoTracking()
            .Where(w => w.WorkingDayId == businessDay.Value.WorkingDayId)
            .ToListAsync(ct);
        var conversions = await _unitConversionService.GetConversionsAsync(
            todayEntries.Select(entry => new ItemUnitKey(entry.ItemId, entry.UnitId)), ct);

        return new WasteSummaryDto(
            todayEntries.Count,
            todayEntries.Sum(w => conversions[new ItemUnitKey(w.ItemId, w.UnitId)].ToBaseQuantity(w.Quantity)),
            todayEntries.Sum(w => w.WasteCost));
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> SaveAsync(
        SaveWasteEntryRequest request,
        CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionWaste);
        // ── Validate quantity > 0 ──
        if (request.Quantity <= 0)
            return (false, "الكمية يجب أن تكون أكبر من الصفر.");

        ItemUnitConversion conversion;
        try
        {
            conversion = await _unitConversionService.GetConversionAsync(
                request.ItemId, request.UnitId, ct);
        }
        catch (InvalidOperationException exception)
        {
            return (false, exception.Message);
        }
        var baseQuantity = conversion.ToBaseQuantity(request.Quantity);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var activeDay = await _days.EnsureActiveWorkingDayAsync(ct);
            await _stockMutationLock.AcquireAsync([request.ItemId], ct);

            // Check availability while holding the transaction-owned item lock.
            var available = await _stock.GetCurrentStockAsync(request.ItemId, ct);
            if (available < baseQuantity)
                return (false, "الكمية التالفة أكبر من الرصيد المتاح للصنف.");

            // Fetch unit cost from item if not provided
            var item = await _db.Items.AsNoTracking().FirstAsync(i => i.Id == request.ItemId, ct);
            var unitCost = request.UnitCost > 0
                ? conversion.ToBaseUnitCost(request.UnitCost)
                : item.PurchasePrice;
            var wasteCost = unitCost * baseQuantity;

            var entry = new WasteEntry
            {
                WorkingDayId = activeDay.Id,
                ItemId = request.ItemId,
                UnitId = conversion.BaseUnitId,
                Quantity = baseQuantity,
                UnitCost = unitCost,
                WasteCost = wasteCost,
                Reason = request.Reason,
                WasteType = WasteType.FinishedProductWaste, // kept for backward compatibility
                Notes = request.Notes
            };
            _db.WasteEntries.Add(entry);

            // We need entry.Id for ReferenceId, so save first then add movement
            await _db.SaveChangesAsync(ct);

            var movement = new InventoryMovement
            {
                WorkingDayId = activeDay.Id,
                ItemId = request.ItemId,
                UnitId = conversion.BaseUnitId,
                Type = InventoryMovementType.Waste,
                Quantity = -baseQuantity,             // movements are always stored in base units
                UnitCost = unitCost,
                ReferenceType = "Waste",
                ReferenceId = entry.Id,
                Notes = $"{request.Reason}{(string.IsNullOrWhiteSpace(request.Notes) ? "" : " - " + request.Notes)}"
            };
            _db.InventoryMovements.Add(movement);

            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditActionKeys.WasteCreated, "WasteEntry", entry.Id, null, request.Reason, ct);
            await tx.CommitAsync(ct);

            return (true, null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to save waste entry for ItemId={ItemId}", request.ItemId);
            return (false, Bakery.Application.UserErrorMessages.FromException(ex));
        }
    }
}
