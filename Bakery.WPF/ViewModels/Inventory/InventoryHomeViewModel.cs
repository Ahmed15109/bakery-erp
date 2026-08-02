using System.Collections.ObjectModel;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class InventoryHomeViewModel : ViewModelBase
{
    private readonly IStockCalculationService _stockService;
    private readonly INavigationService _navigationService;
    private readonly IPermissionService _permissionService;

    public InventoryHomeViewModel(IStockCalculationService stockService, INavigationService navigationService, IPermissionService permissionService)
    {
        _stockService = stockService;
        _navigationService = navigationService;
        _permissionService = permissionService;
        Title = Loc.Inventory;
        Metrics = [];
        _ = RefreshAsync();
    }

    public ObservableCollection<DashboardMetricViewModel> Metrics { get; }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Metrics.Clear();
        var hasAccess = _permissionService.HasPermission(PermissionKeys.InventoryView) || _permissionService.HasPermission(PermissionKeys.ReportsInventory);

        if (hasAccess)
        {
            var stock = await _stockService.GetCurrentStockAsync();
            var lowStock = await _stockService.GetLowStockItemsAsync();
            var valuation = await _stockService.GetStockValuationAsync();

            Metrics.Add(new(Loc.Items, stock.Count.ToString(), "PackageVariant"));
            Metrics.Add(new(Loc.LowStockAlerts, lowStock.Count.ToString(), "AlertCircle"));
            Metrics.Add(new(Loc.Value, valuation.ToString("N2"), "CurrencyUsd"));
        }
        else
        {
            Metrics.Add(new(Loc.Items, Loc.NoPermission, "PackageVariant"));
            Metrics.Add(new(Loc.LowStockAlerts, Loc.NoPermission, "AlertCircle"));
            Metrics.Add(new(Loc.Value, Loc.NoPermission, "CurrencyUsd"));
        }
    }

    [RelayCommand]
    private void OpenItems() => _navigationService.NavigateTo<ItemsViewModel>();

    [RelayCommand]
    private void OpenUnits() => _navigationService.NavigateTo<UnitsViewModel>();

    [RelayCommand]
    private void OpenMovements() => _navigationService.NavigateTo<InventoryMovementsViewModel>();

    [RelayCommand]
    private void OpenStockCount() => _navigationService.NavigateTo<StockCountViewModel>();
}
