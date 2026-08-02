using System.Text.Json;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Engines;

public sealed class ProductionPostingEngine : IProductionPostingEngine
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<ProductionOrder> _productionOrderRepository;
    private readonly IRepository<InventoryMovement> _inventoryMovementRepository;
    private readonly IRepository<EmployeeWage> _employeeWageRepository;
    private readonly IRepository<PartyLedgerEntry> _partyLedgerEntryRepository;
    private readonly IRepository<Item> _itemRepository;
    private readonly IWorkingDayService _workingDayService;
    private readonly IAuditService _auditService;
    private readonly IStockCalculationService _stockService;
    private readonly IItemUnitConversionService _unitConversionService;
    private readonly IStockMutationLock _stockMutationLock;

    public ProductionPostingEngine(
        IUnitOfWork unitOfWork,
        IRepository<ProductionOrder> productionOrderRepository,
        IRepository<InventoryMovement> inventoryMovementRepository,
        IRepository<EmployeeWage> employeeWageRepository,
        IRepository<PartyLedgerEntry> partyLedgerEntryRepository,
        IRepository<Item> itemRepository,
        IWorkingDayService workingDayService,
        IAuditService auditService,
        IStockCalculationService stockService,
        IItemUnitConversionService unitConversionService,
        IStockMutationLock stockMutationLock)
    {
        _unitOfWork = unitOfWork;
        _productionOrderRepository = productionOrderRepository;
        _inventoryMovementRepository = inventoryMovementRepository;
        _employeeWageRepository = employeeWageRepository;
        _partyLedgerEntryRepository = partyLedgerEntryRepository;
        _itemRepository = itemRepository;
        _workingDayService = workingDayService;
        _auditService = auditService;
        _stockService = stockService;
        _unitConversionService = unitConversionService;
        _stockMutationLock = stockMutationLock;
    }

    public async Task PostProductionAsync(int productionOrderId)
    {
        var activeDay = await _workingDayService.EnsureActiveWorkingDayAsync();

        var context = ((dynamic)_productionOrderRepository).DbContext as DbContext;
        if (context == null) throw new InvalidOperationException("DbContext access failed.");
        var order = await GetOrderWithDetailsAsync(productionOrderId);
        if (order == null)
            throw new InvalidOperationException("Production order not found.");

        if (order.Status != ProductionStatus.Draft)
            throw new InvalidOperationException("Only draft production orders can be posted.");

        var unitKeys = order.ConsumedItems
            .Select(item => new ItemUnitKey(item.ItemId, item.UnitId))
            .Concat(order.ProducedItems.Select(item => new ItemUnitKey(item.ItemId, item.UnitId)));
        var conversions = await _unitConversionService.GetConversionsAsync(unitKeys);
        await using var tx = await context.Database.BeginTransactionAsync();
        await _stockMutationLock.AcquireAsync(unitKeys.Select(key => key.ItemId));

        // Hard Stock Validation
        foreach (var consumed in order.ConsumedItems)
        {
            var conversion = conversions[new ItemUnitKey(consumed.ItemId, consumed.UnitId)];
            var required = conversion.ToBaseQuantity(consumed.Quantity);
            var available = await _stockService.GetCurrentStockAsync(consumed.ItemId);
            if (available < required)
            {
                throw new InvalidOperationException($"Insufficient stock for item: {consumed.Item.Name}. Required: {required}, Available: {available}");
            }
        }

        foreach (var consumed in order.ConsumedItems)
        {
            var conversion = conversions[new ItemUnitKey(consumed.ItemId, consumed.UnitId)];
            consumed.Quantity = conversion.ToBaseQuantity(consumed.Quantity);
            consumed.UnitCost = conversion.ToBaseUnitCost(consumed.UnitCost);
            consumed.UnitId = conversion.BaseUnitId;
        }

        foreach (var produced in order.ProducedItems)
        {
            var conversion = conversions[new ItemUnitKey(produced.ItemId, produced.UnitId)];
            produced.ExpectedProducedQty = conversion.ToBaseQuantity(produced.ExpectedProducedQty);
            produced.ActualProducedQty = conversion.ToBaseQuantity(produced.ActualProducedQty);
            produced.VarianceQty = conversion.ToBaseQuantity(produced.VarianceQty);
            produced.UnitCost = conversion.ToBaseUnitCost(produced.UnitCost);
            produced.UnitId = conversion.BaseUnitId;
        }

        var flourItems = order.ConsumedItems
            .Where(ci => ci.Item.Name.Contains("Flour", StringComparison.OrdinalIgnoreCase) || 
                         ci.Item.Name.Contains("دقيق", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var totalFlourConsumed = flourItems.Sum(x => x.Quantity);

        foreach (var emp in order.Employees)
        {
            var employee = emp.Employee;
            if (employee.WageType == WageType.Production)
            {
                // Snapshot the current employee rates
                emp.WageTypeSnapshot = employee.WageType;
                emp.WageAmountSnapshot = employee.ProductionRate;

                decimal wageAmount = totalFlourConsumed * emp.ContributionPercentage * employee.ProductionRate;
                emp.CalculatedWage = wageAmount;

                var employeeWage = new EmployeeWage
                {
                    WorkingDayId = activeDay.Id,
                    EmployeeId = emp.EmployeeId,
                    WageDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    Amount = wageAmount,
                    WageTypeSnapshot = employee.WageType,
                    WageAmountSnapshot = employee.ProductionRate,
                    Notes = $"Production: {order.ProductionNumber}"
                };
                await _employeeWageRepository.AddAsync(employeeWage);

                var ledgerEntry = new PartyLedgerEntry
                {
                    WorkingDayId = activeDay.Id,
                    PartyId = emp.Employee.PartyId,
                    EntryDate = DateTime.UtcNow,
                    Amount = -wageAmount, // Negative = bakery owes employee
                    Description = $"Wages for Production {order.ProductionNumber}",
                    ReferenceType = "ProductionWages",
                    ReferenceId = productionOrderId
                };
                await _partyLedgerEntryRepository.AddAsync(ledgerEntry);
            }
        }

        foreach (var consumed in order.ConsumedItems)
        {
            var movement = new InventoryMovement
            {
                WorkingDayId = activeDay.Id,
                ItemId = consumed.ItemId,
                UnitId = consumed.UnitId,
                Type = InventoryMovementType.ProductionConsume,
                Quantity = -consumed.Quantity,
                UnitCost = consumed.UnitCost,
                ReferenceType = "ProductionOrder",
                ReferenceId = order.Id,
                Notes = Loc.InventoryNoteConsumedForProduction(order.ProductionNumber)
            };
            await _inventoryMovementRepository.AddAsync(movement);
        }

        foreach (var produced in order.ProducedItems)
        {
            var movement = new InventoryMovement
            {
                WorkingDayId = activeDay.Id,
                ItemId = produced.ItemId,
                UnitId = produced.UnitId,
                Type = InventoryMovementType.ProductionProduce,
                Quantity = produced.ActualProducedQty,
                UnitCost = produced.UnitCost,
                ReferenceType = "ProductionOrder",
                ReferenceId = order.Id,
                Notes = Loc.InventoryNoteProducedFromProduction(order.ProductionNumber)
            };
            await _inventoryMovementRepository.AddAsync(movement);
        }

        if (order.Recipe != null)
        {
            order.RecipeSnapshotJson = JsonSerializer.Serialize(order.Recipe, new JsonSerializerOptions 
            { 
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles 
            });
        }

        order.Status = ProductionStatus.Completed;
        order.CompletedAt = DateTime.UtcNow;

        await _productionOrderRepository.UpdateAsync(order);
        await _unitOfWork.SaveChangesAsync();

        await _auditService.LogAsync(AuditActionKeys.ProductionPosted, "ProductionOrder", order.Id, null, order.ProductionNumber);
        await tx.CommitAsync();
    }

    public async Task CancelProductionAsync(int productionOrderId)
    {
        var activeDay = await _workingDayService.EnsureActiveWorkingDayAsync();

        var context = ((dynamic)_productionOrderRepository).DbContext as DbContext;
        if (context == null) throw new InvalidOperationException("DbContext access failed.");
        await using var tx = await context.Database.BeginTransactionAsync();

        var order = await GetOrderWithDetailsAsync(productionOrderId);
        if (order == null)
            throw new InvalidOperationException("Production order not found.");

        if (order.Status != ProductionStatus.Completed)
            throw new InvalidOperationException("Only completed production orders can be cancelled.");

        await _stockMutationLock.AcquireAsync(
            order.ConsumedItems.Select(item => item.ItemId)
                .Concat(order.ProducedItems.Select(item => item.ItemId)));

        var activeProductionMovements = await context.Set<InventoryMovement>()
            .Where(m => m.ReferenceType == "ProductionOrder" && m.ReferenceId == order.Id && !m.IsReversed)
            .ToListAsync();
        foreach (var group in activeProductionMovements.GroupBy(movement => movement.ItemId))
        {
            var netProducedQuantity = group.Sum(movement => movement.Quantity);
            if (netProducedQuantity <= 0) continue;
            var available = await _stockService.GetCurrentStockAsync(group.Key);
            if (available < netProducedQuantity)
            {
                var itemName = order.ProducedItems.FirstOrDefault(item => item.ItemId == group.Key)?.Item?.Name ?? group.Key.ToString();
                throw new InvalidOperationException(
                    $"لا يمكن التراجع عن أمر الإنتاج لأن الكمية المتاحة من {itemName} هي {available:N3} بينما يلزم {netProducedQuantity:N3}. راجع المبيعات أو الحركات التابعة أولاً.");
            }
        }

        order.Status = ProductionStatus.Cancelled;
        await _productionOrderRepository.UpdateAsync(order);

        // Reverse inventory movements
        var movements = activeProductionMovements;

        foreach (var movement in movements)
        {
            movement.IsReversed = true;
            context.Set<InventoryMovement>().Update(movement);

            var reversal = new InventoryMovement
            {
                WorkingDayId = activeDay.Id,
                ItemId = movement.ItemId,
                UnitId = movement.UnitId,
                Type = InventoryMovementType.Adjustment,
                Quantity = -movement.Quantity,
                UnitCost = movement.UnitCost,
                ReferenceType = "ProductionCancel",
                ReferenceId = order.Id,
                ReversalReferenceId = movement.Id,
                Notes = Loc.InventoryNoteReversal(movement.Notes)
            };
            await _inventoryMovementRepository.AddAsync(reversal);
        }

        // Reverse Employee Wages
        var wages = await context.Set<EmployeeWage>()
            .Where(w => w.Notes!.Contains(order.ProductionNumber) && !w.IsReversed)
            .ToListAsync();
            
        foreach (var wage in wages)
        {
            wage.IsReversed = true;
            context.Set<EmployeeWage>().Update(wage);
            
            var reversalWage = new EmployeeWage
            {
                WorkingDayId = activeDay.Id,
                EmployeeId = wage.EmployeeId,
                WageDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Amount = -wage.Amount,
                ReversalReferenceId = wage.Id,
                Notes = $"Reversal of Production: {order.ProductionNumber}"
            };
            await _employeeWageRepository.AddAsync(reversalWage);
        }

        // Reverse Party Ledger Entries
        var ledgers = await context.Set<PartyLedgerEntry>()
            .Where(l => l.ReferenceType == "ProductionWages" && l.ReferenceId == order.Id && !l.IsReversed)
            .ToListAsync();

        foreach (var ledger in ledgers)
        {
            ledger.IsReversed = true;
            context.Set<PartyLedgerEntry>().Update(ledger);

            var reversalLedger = new PartyLedgerEntry
            {
                WorkingDayId = activeDay.Id,
                PartyId = ledger.PartyId,
                EntryDate = DateTime.UtcNow,
                Amount = -ledger.Amount,
                Description = $"Reversal of Wages for Production {order.ProductionNumber}",
                ReferenceType = "ProductionCancel",
                ReferenceId = order.Id,
                ReversalReferenceId = ledger.Id
            };
            await _partyLedgerEntryRepository.AddAsync(reversalLedger);
        }

        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.ProductionCancelled, "ProductionOrder", order.Id, null, order.ProductionNumber);
        await tx.CommitAsync();
    }

    private async Task<ProductionOrder?> GetOrderWithDetailsAsync(int id)
    {
        // Using Repository interface might not support Include, so we need a workaround or assume the injected repository supports it.
        // For EF Core we might just cast to DbContext to get the includes for the engine since it's an infrastructure service.
        var context = ((dynamic)_productionOrderRepository).DbContext as DbContext;
        if (context == null) throw new InvalidOperationException("DbContext access failed.");

        return await context.Set<ProductionOrder>()
            .Include(o => o.ConsumedItems).ThenInclude(c => c.Item)
            .Include(o => o.ProducedItems)
            .Include(o => o.Employees).ThenInclude(e => e.Employee).ThenInclude(e => e.JobRole)
            .Include(o => o.Recipe)
            .FirstOrDefaultAsync(o => o.Id == id);
    }
}
