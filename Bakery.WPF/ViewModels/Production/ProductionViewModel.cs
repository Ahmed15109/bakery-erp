using System.Collections.ObjectModel;
using Bakery.Application.Interfaces;
using Bakery.Application.DTOs;
using Bakery.Domain.Entities;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class ProductionViewModel : ViewModelBase
{
    private readonly IProductionService _productionService;
    private readonly INavigationService _navigationService;

    public ProductionViewModel(IProductionService productionService, INavigationService navigationService)
    {
        _productionService = productionService;
        _navigationService = navigationService;
        Title = Loc.ProductionView;
        _ = LoadSummaryAsync();
    }

    [ObservableProperty] private int totalRecipes;
    [ObservableProperty] private int todayOrdersCount;
    [ObservableProperty] private decimal todayProductionCost;
    [ObservableProperty] private decimal todayProducedValue;
    [ObservableProperty] private ProductionOrder? selectedBlockingOrder;

    [RelayCommand]
    private async Task LoadSummaryAsync()
    {
        var summary = await _productionService.GetProductionSummaryAsync();
        TotalRecipes = summary.TotalRecipes;
        TodayOrdersCount = summary.TodayOrdersCount;
        TodayProductionCost = summary.TodayProductionCost;
        TodayProducedValue = summary.TodayProducedValue;
    }

    [RelayCommand]
    private void NavigateToNewOrder() => _navigationService.NavigateTo<ProductionOrderViewModel>();

    public async Task ShowBlockingOrderAsync(int orderId)
    {
        SelectedBlockingOrder = (await _productionService.GetAllProductionOrdersAsync())
            .SingleOrDefault(order => order.Id == orderId);
    }
}
