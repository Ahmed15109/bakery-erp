using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class ProductionService : IProductionService
{
    private readonly IRepository<ProductionOrder> _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductionPostingEngine _postingEngine;
    private readonly IAuditService _auditService;
    private readonly IStockCalculationService _stockService;
    private readonly IRecipeService _recipeService;
    private readonly IPermissionService _permissionService;
    private readonly IItemUnitConversionService _unitConversionService;
    private readonly IBusinessDateService _businessDateService;

    public ProductionService(
        IRepository<ProductionOrder> repository, 
        IUnitOfWork unitOfWork, 
        IProductionPostingEngine postingEngine,
        IAuditService auditService,
        IStockCalculationService stockService,
        IRecipeService recipeService,
        IPermissionService permissionService,
        IItemUnitConversionService unitConversionService,
        IBusinessDateService businessDateService)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _postingEngine = postingEngine;
        _auditService = auditService;
        _stockService = stockService;
        _recipeService = recipeService;
        _permissionService = permissionService;
        _unitConversionService = unitConversionService;
        _businessDateService = businessDateService;
    }

    public async Task<ProductionOrder> CreateProductionOrderAsync(ProductionOrder order)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionCreate);
        await NormalizeOrderUnitsAsync(order);
        var added = await _repository.AddAsync(order);
        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.Create, "ProductionOrder", added.Id, null, added.ProductionNumber);
        return added;
    }

    public async Task DeleteProductionOrderAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionEdit);
        var order = await _repository.GetByIdAsync(id);
        if (order != null && order.Status == ProductionStatus.Draft)
        {
            await _repository.DeleteAsync(order);
            await _unitOfWork.SaveChangesAsync();
            await _auditService.LogAsync(AuditActionKeys.Delete, "ProductionOrder", id, null, order.ProductionNumber);
        }
    }

    public async Task<IEnumerable<ProductionOrder>> GetAllProductionOrdersAsync()
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.ProductionView, PermissionKeys.ReportsProduction);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return await _repository.ListAsync();

        return await context.Set<ProductionOrder>()
            .Include(o => o.WorkingDay)
            .Include(o => o.Recipe)
            .OrderByDescending(o => o.StartedAt)
            .ToListAsync();
    }

    public async Task<ProductionOrder?> GetProductionOrderByIdAsync(int id)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.ProductionView, PermissionKeys.ReportsProduction);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return await _repository.GetByIdAsync(id);

        return await context.Set<ProductionOrder>()
            .Include(o => o.WorkingDay)
            .Include(o => o.Recipe)
            .Include(o => o.ConsumedItems).ThenInclude(ci => ci.Item)
            .Include(o => o.ProducedItems).ThenInclude(pi => pi.Item)
            .Include(o => o.Employees).ThenInclude(e => e.Employee)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task UpdateProductionOrderAsync(ProductionOrder order)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionEdit);
        await NormalizeOrderUnitsAsync(order);
        await _repository.UpdateAsync(order);
        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.Update, "ProductionOrder", order.Id, null, order.ProductionNumber);
    }

    public Task PostProductionOrderAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionEdit);
        return _postingEngine.PostProductionAsync(id);
    }

    public Task CancelProductionOrderAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionCancel);
        return _postingEngine.CancelProductionAsync(id);
    }

    public async Task<ProductionSummaryDto> GetProductionSummaryAsync()
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.ProductionView, PermissionKeys.ReportsProduction);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return new ProductionSummaryDto(0, 0, 0, 0);

        var totalRecipes = await context.Set<Recipe>().CountAsync(r => r.IsActive);
        var businessDay = await _businessDateService.GetCurrentAsync();
        if (businessDay is null) return new ProductionSummaryDto(totalRecipes, 0, 0, 0);

        var todayOrders = context.Set<ProductionOrder>()
            .AsNoTracking()
            .Where(o => o.WorkingDayId == businessDay.Value.WorkingDayId && o.Status == ProductionStatus.Completed);
        var todayOrdersCount = await todayOrders.CountAsync();

        var canViewCost = _permissionService.HasAnyPermission(
            PermissionKeys.ProductsViewCost,
            PermissionKeys.ReportsProduction);
        decimal totalCost = canViewCost
            ? await context.Set<ProductionConsumedItem>()
                .AsNoTracking()
                .Where(item => item.ProductionOrder.WorkingDayId == businessDay.Value.WorkingDayId &&
                    item.ProductionOrder.Status == ProductionStatus.Completed)
                .SumAsync(item => (decimal?)(item.Quantity * item.UnitCost)) ?? 0m
            : 0m;
        decimal totalValue = canViewCost
            ? await context.Set<ProductionProducedItem>()
                .AsNoTracking()
                .Where(item => item.ProductionOrder.WorkingDayId == businessDay.Value.WorkingDayId &&
                    item.ProductionOrder.Status == ProductionStatus.Completed)
                .SumAsync(item => (decimal?)(item.ActualProducedQty * item.UnitCost)) ?? 0m
            : 0m;

        return new ProductionSummaryDto(
            totalRecipes,
            todayOrdersCount,
            totalCost,
            totalValue
        );
    }

    public async Task<StockValidationResult> ValidateProductionStockAsync(int recipeId, decimal multiplier)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.ProductionCreate, PermissionKeys.ProductionEdit);
        var recipe = await _recipeService.GetRecipeByIdAsync(recipeId);
        if (recipe == null) throw new ArgumentException("Recipe not found");

        var missingItems = new List<MissingStockDto>();
        foreach (var item in recipe.ConsumedItems)
        {
            var conversion = await _unitConversionService.GetConversionAsync(item.RawItemId, item.UnitId);
            var required = conversion.ToBaseQuantity(item.Quantity * multiplier);
            var available = await _stockService.GetCurrentStockAsync(item.RawItemId);
            
            if (available < required)
            {
                missingItems.Add(new MissingStockDto(
                    item.RawItemId,
                    item.RawItem.Name,
                    required,
                    available,
                    item.Unit.Name
                ));
            }
        }

        return new StockValidationResult(missingItems.Count == 0, missingItems);
    }

    public async Task<StockValidationResult> ValidateProductionItemsStockAsync(IEnumerable<ProductionConsumedItem> items)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.ProductionCreate, PermissionKeys.ProductionEdit);
        var missingItems = new List<MissingStockDto>();
        foreach (var item in items)
        {
            var conversion = await _unitConversionService.GetConversionAsync(item.ItemId, item.UnitId);
            var required = conversion.ToBaseQuantity(item.Quantity);
            var available = await _stockService.GetCurrentStockAsync(item.ItemId);
            if (available < required)
            {
                missingItems.Add(new MissingStockDto(
                    item.ItemId,
                    item.Item?.Name ?? "صنف غير معروف",
                    required,
                    available,
                    item.Unit?.Name ?? ""
                ));
            }
        }
        return new StockValidationResult(missingItems.Count == 0, missingItems);
    }

    private async Task NormalizeOrderUnitsAsync(ProductionOrder order)
    {
        var keys = order.ConsumedItems
            .Select(item => new ItemUnitKey(item.ItemId, item.UnitId))
            .Concat(order.ProducedItems.Select(item => new ItemUnitKey(item.ItemId, item.UnitId)));
        var conversions = await _unitConversionService.GetConversionsAsync(keys);

        foreach (var item in order.ConsumedItems)
        {
            var conversion = conversions[new ItemUnitKey(item.ItemId, item.UnitId)];
            item.Quantity = conversion.ToBaseQuantity(item.Quantity);
            item.UnitCost = conversion.ToBaseUnitCost(item.UnitCost);
            item.UnitId = conversion.BaseUnitId;
        }

        foreach (var item in order.ProducedItems)
        {
            var conversion = conversions[new ItemUnitKey(item.ItemId, item.UnitId)];
            item.ExpectedProducedQty = conversion.ToBaseQuantity(item.ExpectedProducedQty);
            item.ActualProducedQty = conversion.ToBaseQuantity(item.ActualProducedQty);
            item.VarianceQty = conversion.ToBaseQuantity(item.VarianceQty);
            item.UnitCost = conversion.ToBaseUnitCost(item.UnitCost);
            item.UnitId = conversion.BaseUnitId;
        }
    }
}
