using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Bakery.Shared.Helpers;

namespace Bakery.Infrastructure.Services;

public sealed class InventoryService : IInventoryService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IWorkingDayService _workingDayService;
    private readonly IStockCalculationService _stockCalculationService;
    private readonly IPermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly IUserSessionService _userSessionService;
    private readonly IValidator<InventoryAdjustmentRequest> _adjustmentValidator;
    private readonly IValidator<CompleteStockCountRequest> _stockCountValidator;
    private readonly IItemUnitConversionService _unitConversionService;
    private readonly IStockMutationLock _stockMutationLock;

    public InventoryService(BakeryDbContext dbContext, IWorkingDayService workingDayService, IStockCalculationService stockCalculationService, IPermissionService permissionService, IAuditService auditService, IUserSessionService userSessionService, IValidator<InventoryAdjustmentRequest> adjustmentValidator, IValidator<CompleteStockCountRequest> stockCountValidator, IItemUnitConversionService unitConversionService, IStockMutationLock stockMutationLock)
    {
        _dbContext = dbContext;
        _workingDayService = workingDayService;
        _stockCalculationService = stockCalculationService;
        _permissionService = permissionService;
        _auditService = auditService;
        _userSessionService = userSessionService;
        _adjustmentValidator = adjustmentValidator;
        _stockCountValidator = stockCountValidator;
        _unitConversionService = unitConversionService;
        _stockMutationLock = stockMutationLock;
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> AdjustStockAsync(InventoryAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.InventoryStockAdjustments)) return (false, Loc.ErrAdminRequired);
        var validation = await _adjustmentValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return (false, validation.Errors[0].ErrorMessage);
        ItemUnitConversion conversion;
        try
        {
            conversion = await _unitConversionService.GetConversionAsync(
                request.ItemId, request.UnitId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return (false, exception.Message);
        }
        var day = await _workingDayService.EnsureActiveWorkingDayAsync(cancellationToken);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _stockMutationLock.AcquireAsync([request.ItemId], cancellationToken);

        var baseQuantity = conversion.ToBaseQuantity(request.Quantity);
        var signedQuantity = request.IsIncrease ? baseQuantity : -baseQuantity;
        var allowNegative = await _dbContext.AppSettings.AnyAsync(setting => setting.Key == "Inventory.AllowNegativeStock" && setting.Value == "true", cancellationToken);
        if (signedQuantity < 0 && !allowNegative && !await _stockCalculationService.HasAvailableStockAsync(request.ItemId, baseQuantity, cancellationToken))
        {
            return (false, Loc.ErrNotEnoughStock);
        }

        var item = await _dbContext.Items.FirstAsync(entity => entity.Id == request.ItemId, cancellationToken);
        _dbContext.InventoryMovements.Add(new InventoryMovement
        {
            WorkingDayId = day.Id,
            ItemId = request.ItemId,
            UnitId = conversion.BaseUnitId,
            Type = InventoryMovementType.Adjustment,
            Quantity = signedQuantity,
            UnitCost = item.PurchasePrice,
            ReferenceType = "InventoryAdjustment",
            Notes = request.Reason
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(AuditActionKeys.InventoryAdjusted, nameof(InventoryMovement), request.ItemId, null, request.Reason, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (true, null);
    }

    public async Task<IReadOnlyList<InventoryMovementDto>> GetMovementHistoryAsync(DateTime? from, DateTime? to, int? itemId, InventoryMovementType? type, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.InventoryView, PermissionKeys.ReportsInventory);
        var query = _dbContext.InventoryMovements
            .Include(movement => movement.Item).ThenInclude(item => item.BaseUnit)
            .Include(movement => movement.Unit)
            .AsNoTracking();
        if (from.HasValue) query = query.Where(movement => movement.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(movement => movement.CreatedAt <= to.Value);
        if (itemId.HasValue) query = query.Where(movement => movement.ItemId == itemId.Value);
        if (type.HasValue) query = query.Where(movement => movement.Type == type.Value);
        var list = await query.OrderBy(movement => movement.ItemId).ThenBy(movement => movement.CreatedAt).ToListAsync(cancellationToken);
        var conversions = await _unitConversionService.GetConversionsAsync(
            list.Select(movement => new ItemUnitKey(movement.ItemId, movement.UnitId)),
            cancellationToken);
        var balances = new Dictionary<int, decimal>();
        return list.Select(movement =>
        {
            var conversion = conversions[new ItemUnitKey(movement.ItemId, movement.UnitId)];
            var baseQuantity = conversion.ToBaseQuantity(movement.Quantity);
            balances.TryGetValue(movement.ItemId, out var balance);
            balance += baseQuantity;
            balances[movement.ItemId] = balance;
            return new InventoryMovementDto(
                movement.Id,
                movement.CreatedAt,
                movement.Item.Code,
                movement.Item.Name,
                movement.Item.BaseUnit.Symbol,
                movement.Type,
                baseQuantity,
                conversion.ToBaseUnitCost(movement.UnitCost),
                balance,
                Loc.LocalizeInventoryMovementNote(movement.Notes));
        }).ToList();
    }

    public async Task<int> StartStockCountAsync(StartStockCountRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.InventoryCount);
        var session = new StockCountSession { StartedBy = _userSessionService.CurrentUser?.UserName ?? "system", Notes = request.Notes };
        _dbContext.StockCountSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(AuditActionKeys.StockCountStarted, nameof(StockCountSession), session.Id, null, request.Notes, cancellationToken);
        return session.Id;
    }

    public async Task<IReadOnlyList<StockCountLineDto>> GetStockCountLinesAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.InventoryCount);
        var stock = await _stockCalculationService.GetCurrentStockAsync(cancellationToken);
        return stock.Select(item => new StockCountLineDto(item.ItemId, item.Code, item.Name, 0, item.Unit, item.Quantity, item.Quantity, 0)).ToList();
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> CompleteStockCountAsync(CompleteStockCountRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.InventoryCount);
        var validation = await _stockCountValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return (false, validation.Errors[0].ErrorMessage);
        var day = await _workingDayService.EnsureActiveWorkingDayAsync(cancellationToken);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var session = await _dbContext.StockCountSessions.Include(entity => entity.Lines).FirstAsync(entity => entity.Id == request.SessionId, cancellationToken);
        if (session.IsCompleted) return (false, Loc.ErrStockCountClosed);
        var itemIds = request.Lines.Select(line => line.ItemId).Distinct().ToArray();
        await _stockMutationLock.AcquireAsync(itemIds, cancellationToken);
        var items = await _dbContext.Items
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var requestedUnits = request.Lines.Select(line => new ItemUnitKey(
            line.ItemId,
            line.UnitId == 0 ? items[line.ItemId].BaseUnitId : line.UnitId));
        IReadOnlyDictionary<ItemUnitKey, ItemUnitConversion> conversions;
        try
        {
            conversions = await _unitConversionService.GetConversionsAsync(requestedUnits, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return (false, exception.Message);
        }
        var systemStock = await _stockCalculationService.GetCurrentStockAsync(itemIds, cancellationToken);
        foreach (var line in request.Lines)
        {
            var item = items[line.ItemId];
            var requestedUnitId = line.UnitId == 0 ? item.BaseUnitId : line.UnitId;
            var conversion = conversions[new ItemUnitKey(line.ItemId, requestedUnitId)];
            var systemQuantity = systemStock.GetValueOrDefault(line.ItemId);
            var physicalQuantity = conversion.ToBaseQuantity(line.PhysicalQuantity);
            var variance = physicalQuantity - systemQuantity;
            session.Lines.Add(new StockCountLine { ItemId = line.ItemId, UnitId = item.BaseUnitId, SystemQuantity = systemQuantity, PhysicalQuantity = physicalQuantity, VarianceQuantity = variance });
            if (variance != 0)
            {
                _dbContext.InventoryMovements.Add(new InventoryMovement
                {
                    WorkingDayId = day.Id,
                    ItemId = line.ItemId,
                    UnitId = item.BaseUnitId,
                    Type = InventoryMovementType.Adjustment,
                    Quantity = variance,
                    UnitCost = item.PurchasePrice,
                    ReferenceType = "StockCount",
                    ReferenceId = session.Id,
                    Notes = Loc.InventoryNoteStockCountVariance(session.Id)
                });
            }
        }

        session.IsCompleted = true;
        session.CompletedAt = DateTime.UtcNow;
        session.CompletedBy = _userSessionService.CurrentUser?.UserName ?? "system";
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(AuditActionKeys.StockCountCompleted, nameof(StockCountSession), session.Id, null, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (true, null);
    }
}
