using System.Collections.ObjectModel;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class StockCountViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IMessageService _messageService;
    private readonly INavigationService _navigationService;
    public StockCountViewModel(IInventoryService inventoryService, IMessageService messageService, INavigationService navigationService)
    {
        _inventoryService = inventoryService; _messageService = messageService; _navigationService = navigationService; Title = Loc.StockCount; Lines = [];
    }
    public ObservableCollection<StockCountLineDto> Lines { get; }
    [ObservableProperty] private int sessionId;
    [ObservableProperty] private string? notes;

    [RelayCommand]
    private void Back() => _navigationService.NavigateTo<InventoryHomeViewModel>();

    [RelayCommand]
    private async Task StartAsync()
    {
        SessionId = await _inventoryService.StartStockCountAsync(new StartStockCountRequest(Notes));
        Lines.Clear();
        foreach (var line in await _inventoryService.GetStockCountLinesAsync(SessionId)) Lines.Add(line);
    }

    [RelayCommand]
    private async Task CompleteAsync()
    {
        if (SessionId == 0) return;
        var result = await _inventoryService.CompleteStockCountAsync(new CompleteStockCountRequest(SessionId, Lines));
        if (!result.Succeeded) { _messageService.ShowError(result.ErrorMessage ?? "فشل إتمام عملية الجرد."); return; }
        _messageService.ShowInfo("تم إتمام الجرد بنجاح.");
    }
}

public sealed partial class InventoryMovementsViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IItemService _itemService;
    private readonly INavigationService _navigationService;
    public InventoryMovementsViewModel(IInventoryService inventoryService, IItemService itemService, INavigationService navigationService)
    {
        _inventoryService = inventoryService; _itemService = itemService; _navigationService = navigationService; Title = Loc.Movements; Movements = []; Items = []; MovementTypes = Enum.GetValues<InventoryMovementType>(); _ = LoadAsync();
    }
    public ObservableCollection<InventoryMovementDto> Movements { get; }
    public ObservableCollection<ItemDto> Items { get; }
    public IReadOnlyList<InventoryMovementType> MovementTypes { get; }
    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private ItemDto? selectedItem;
    [ObservableProperty] private InventoryMovementType? selectedMovementType;

    [RelayCommand]
    private void Back() => _navigationService.NavigateTo<InventoryHomeViewModel>();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (Items.Count == 0) foreach (var item in await _itemService.SearchAsync(null, null)) Items.Add(item);
        Movements.Clear();
        foreach (var movement in await _inventoryService.GetMovementHistoryAsync(FromDate, ToDate, SelectedItem?.Id, SelectedMovementType)) Movements.Add(movement);
    }
}
