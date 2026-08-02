using Bakery.Domain.Entities;

namespace Bakery.Application.Interfaces;

public interface IRecipeService
{
    Task<IEnumerable<Recipe>> GetAllRecipesAsync();
    Task<Recipe?> GetRecipeByIdAsync(int id);
    Task<Recipe> CreateRecipeAsync(Recipe recipe);
    Task UpdateRecipeAsync(Recipe recipe);
    Task DeleteRecipeAsync(int id);
    Task<decimal> CalculateRecipeCostAsync(int recipeId);
    Task<Recipe?> GetRecipeByProducedItemIdAsync(int itemId);
}
