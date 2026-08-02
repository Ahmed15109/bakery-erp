using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class RecipeService : IRecipeService
{
    private readonly IRepository<Recipe> _recipeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly IPermissionService _permissionService;
    private readonly IItemUnitConversionService _unitConversionService;

    public RecipeService(IRepository<Recipe> recipeRepository, IUnitOfWork unitOfWork, IAuditService auditService, IPermissionService permissionService, IItemUnitConversionService unitConversionService)
    {
        _recipeRepository = recipeRepository;
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _permissionService = permissionService;
        _unitConversionService = unitConversionService;
    }

    public async Task<Recipe> CreateRecipeAsync(Recipe recipe)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionCreate);
        await NormalizeConsumedUnitsAsync(recipe);
        var added = await _recipeRepository.AddAsync(recipe);
        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.Create, "Recipe", added.Id, null, added.Name);
        return added;
    }

    public async Task DeleteRecipeAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionEdit);
        var recipe = await _recipeRepository.GetByIdAsync(id);
        if (recipe != null)
        {
            await _recipeRepository.DeleteAsync(recipe);
            await _unitOfWork.SaveChangesAsync();
            await _auditService.LogAsync(AuditActionKeys.Delete, "Recipe", id, null, recipe.Name);
        }
    }

    public async Task<IEnumerable<Recipe>> GetAllRecipesAsync()
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionView);
        var context = ((dynamic)_recipeRepository).DbContext as DbContext;
        if (context == null) return await _recipeRepository.ListAsync();

        return await context.Set<Recipe>()
            .Include(r => r.ProducedItem)
            .Include(r => r.ConsumedItems).ThenInclude(ci => ci.RawItem)
            .Include(r => r.ConsumedItems).ThenInclude(ci => ci.Unit)
            .ToListAsync();
    }

    public async Task<Recipe?> GetRecipeByIdAsync(int id)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionView);
        var context = ((dynamic)_recipeRepository).DbContext as DbContext;
        if (context == null) return await _recipeRepository.GetByIdAsync(id);

        return await context.Set<Recipe>()
            .Include(r => r.ProducedItem)
            .Include(r => r.ConsumedItems).ThenInclude(ci => ci.RawItem)
            .Include(r => r.ConsumedItems).ThenInclude(ci => ci.Unit)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task UpdateRecipeAsync(Recipe recipe)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionEdit);
        await NormalizeConsumedUnitsAsync(recipe);
        await _recipeRepository.UpdateAsync(recipe);
        await _unitOfWork.SaveChangesAsync();
        await _auditService.LogAsync(AuditActionKeys.Update, "Recipe", recipe.Id, null, recipe.Name);
    }

    public async Task<decimal> CalculateRecipeCostAsync(int recipeId)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsViewCost);
        var recipe = await GetRecipeByIdAsync(recipeId);
        if (recipe == null) return 0;
        return recipe.ConsumedItems.Sum(item => item.Quantity * item.RawItem.PurchasePrice);
    }

    public async Task<Recipe?> GetRecipeByProducedItemIdAsync(int itemId)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductionView);
        var context = ((dynamic)_recipeRepository).DbContext as DbContext;
        if (context == null) return null;

        return await context.Set<Recipe>()
            .Include(r => r.ProducedItem)
            .Include(r => r.ConsumedItems).ThenInclude(ci => ci.RawItem)
            .Include(r => r.ConsumedItems).ThenInclude(ci => ci.Unit)
            .FirstOrDefaultAsync(r => r.ProducedItemId == itemId && r.IsActive);
    }

    private async Task NormalizeConsumedUnitsAsync(Recipe recipe)
    {
        var conversions = await _unitConversionService.GetConversionsAsync(
            recipe.ConsumedItems.Select(item => new ItemUnitKey(item.RawItemId, item.UnitId)));
        foreach (var item in recipe.ConsumedItems)
        {
            var conversion = conversions[new ItemUnitKey(item.RawItemId, item.UnitId)];
            item.Quantity = conversion.ToBaseQuantity(item.Quantity);
            item.UnitId = conversion.BaseUnitId;
        }
    }
}
