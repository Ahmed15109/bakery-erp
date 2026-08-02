using System.Collections.ObjectModel;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Shared.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class RecipesViewModel : ViewModelBase
{
    private readonly IRecipeService _recipeService;
    private readonly IItemService _itemService;
    private readonly IUnitService _unitService;

    public RecipesViewModel(IRecipeService recipeService, IItemService itemService, IUnitService unitService)
    {
        _recipeService = recipeService;
        _itemService = itemService;
        _unitService = unitService;
        Title = Loc.RecipesView;
        _ = RefreshAsync();
    }

    public ObservableCollection<Recipe> Recipes { get; } = [];
    
    [ObservableProperty] private Recipe? selectedRecipe;
    [ObservableProperty] private bool isEditing;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Recipes.Clear();
        var list = await _recipeService.GetAllRecipesAsync();
        foreach (var item in list) Recipes.Add(item);
    }

    [RelayCommand]
    private async Task DeleteRecipeAsync(Recipe recipe)
    {
        if (recipe == null) return;
        await _recipeService.DeleteRecipeAsync(recipe.Id);
        await RefreshAsync();
    }

   
}
