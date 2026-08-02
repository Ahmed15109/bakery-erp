using System.Collections.ObjectModel;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Shared.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class ProductionHistoryViewModel : ViewModelBase
{
    private readonly IProductionService _service;

    public ProductionHistoryViewModel(IProductionService service)
    {
        _service = service;
        Title = "سجل الإنتاج";
        _ = RefreshAsync();
    }

    public ObservableCollection<ProductionOrder> Orders { get; } = [];
    
    [ObservableProperty] private ProductionOrder? selectedOrder;
    [ObservableProperty] private string searchText = string.Empty;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Orders.Clear();
        var list = await _service.GetAllProductionOrdersAsync();
        foreach (var item in list) Orders.Add(item);
    }

    [RelayCommand]
    private void ViewDetails(ProductionOrder order)
    {
        SelectedOrder = order;
        // Logic to show details dialog
    }
}
