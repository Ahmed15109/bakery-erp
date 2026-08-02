using System.Collections.ObjectModel;
using Bakery.Application.Interfaces;
using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Inventory;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakery.Application.Security;

namespace Bakery.WPF.ViewModels;

public sealed partial class ProductionOrderViewModel : ViewModelBase
{
    private readonly IProductionService _productionService;
    private readonly IRecipeService _recipeService;
    private readonly IItemService _itemService;
    private readonly INavigationService _navigationService;
    private readonly IWorkingDayService _workingDayService;
    private readonly IMessageService _messageService;
    private readonly IPermissionService _permissionService;

    private List<ItemDto> _itemIndex = [];

    public ProductionOrderViewModel(
        IProductionService productionService,
        IRecipeService recipeService,
        IItemService itemService,
        INavigationService navigationService,
        IWorkingDayService workingDayService,
        IMessageService messageService,
        IPermissionService permissionService)
    {
        _productionService = productionService;
        _recipeService = recipeService;
        _itemService = itemService;
        _navigationService = navigationService;
        _workingDayService = workingDayService;
        _messageService = messageService;
        _permissionService = permissionService;
        
        Title = "فاتورة إنتاج جديدة";
        Lines = [];
        Lines.CollectionChanged += (_, _) => { ReIndexLines(); RefreshTotals(); };
        
        ProducedItems = [];
        _ = InitializeAsync();
    }

    public event Action<string>? RequestFocus;

    private async Task InitializeAsync()
    {
        _itemIndex = (await _itemService.SearchAsync(null, null)).ToList();
        foreach (var item in _itemIndex) ProducedItems.Add(item);
        
        RequestFocus?.Invoke("ProducedItemInput");
    }

    [ObservableProperty] private ItemDto? selectedProducedItem;
    [ObservableProperty] private decimal producedQuantity;
    [ObservableProperty] private string? notes;

    partial void OnSelectedProducedItemChanged(ItemDto? value) => _ = ValidateStockAsync();
    partial void OnProducedQuantityChanged(decimal value) => _ = ValidateStockAsync();

    [ObservableProperty] private string itemSearchCode = string.Empty;
    [ObservableProperty] private ObservableCollection<ItemDto> itemSuggestions = [];
    [ObservableProperty] private ItemDto? selectedSuggestion;
    [ObservableProperty] private bool isSuggestionsOpen;

    [ObservableProperty] private ItemDto? pendingItem;
    [ObservableProperty] private decimal entryQuantity = 1;

    [ObservableProperty] private string? validationMessage;
    [ObservableProperty] private bool isValid = true;

    public ObservableCollection<ProductionLineEditor> Lines { get; }
    public ObservableCollection<ItemDto> ProducedItems { get; }

    public decimal TotalConsumedCost => Lines.Sum(x => x.TotalCost);

    private void ReIndexLines()
    {
        for (int i = 0; i < Lines.Count; i++) Lines[i].Index = i + 1;
    }

    private void RefreshTotals() => OnPropertyChanged(nameof(TotalConsumedCost));

    partial void OnItemSearchCodeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ItemSuggestions.Clear();
            IsSuggestionsOpen = false;
            return;
        }
        SearchItems(value);
    }

    private void SearchItems(string search)
    {
        var results = _itemIndex
            .Where(i => i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || i.Code.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();
            
        ItemSuggestions.Clear();
        foreach (var r in results) ItemSuggestions.Add(r);
        IsSuggestionsOpen = ItemSuggestions.Count > 0;
        if (ItemSuggestions.Count > 0) SelectedSuggestion = ItemSuggestions[0];
    }

    [RelayCommand]
    private void MoveSuggestion(string directionText)
    {
        if (!int.TryParse(directionText, out var direction) || ItemSuggestions.Count == 0) return;
        var currentIndex = SelectedSuggestion is null ? -1 : ItemSuggestions.IndexOf(SelectedSuggestion);
        var nextIndex = Math.Clamp(currentIndex + direction, 0, ItemSuggestions.Count - 1);
        SelectedSuggestion = ItemSuggestions[nextIndex];
        IsSuggestionsOpen = true;
    }

    [RelayCommand]
    private void ProcessCodeEntry()
    {
        var item = SelectedSuggestion ?? ItemSuggestions.FirstOrDefault();
        if (item != null)
        {
            ApplyPendingItem(item);
        }
    }

    private void ApplyPendingItem(ItemDto item)
    {
        PendingItem = item;
        EntryQuantity = 1;
        IsSuggestionsOpen = false;
        ItemSearchCode = item.Name;
        RequestFocus?.Invoke("EntryQtyInput");
    }

    [RelayCommand]
    private void AddPendingLine()
    {
        if (PendingItem == null) return;
        
        AddOrMergeItem(PendingItem, EntryQuantity);
        
        PendingItem = null;
        ItemSearchCode = "";
        EntryQuantity = 1;
        RequestFocus?.Invoke("CodeInput");
    }

    private void AddOrMergeItem(ItemDto item, decimal qty)
    {
        var existing = Lines.FirstOrDefault(l => l.ItemId == item.Id);
        if (existing != null)
        {
            existing.Quantity += qty;
        }
        else
        {
            Lines.Add(new ProductionLineEditor
            {
                ItemId = item.Id,
                ItemName = item.Name,
                UnitId = item.BaseUnitId,
                UnitName = item.BaseUnit,
                Quantity = qty,
                UnitCost = item.PurchasePrice
            });
        }
        _ = ValidateStockAsync();
    }

    [RelayCommand]
    private void RemoveLine(ProductionLineEditor line)
    {
        Lines.Remove(line);
        _ = ValidateStockAsync();
    }

    [RelayCommand]
    private async Task LoadRecipeAsync()
    {
        if (SelectedProducedItem == null) return;
        
        var recipe = await _recipeService.GetRecipeByProducedItemIdAsync(SelectedProducedItem.Id);
        if (recipe == null)
        {
            _messageService.ShowInfo("لا توجد وصفة مسجلة لهذا الصنف");
            return;
        }

        Lines.Clear();
        foreach (var item in recipe.ConsumedItems)
        {
            Lines.Add(new ProductionLineEditor
            {
                ItemId = item.RawItemId,
                ItemName = item.RawItem.Name,
                UnitId = item.UnitId,
                UnitName = item.Unit.Name,
                Quantity = item.Quantity,
                UnitCost = item.RawItem.PurchasePrice
            });
        }
        ProducedQuantity = recipe.ProducedQuantity;
        _ = ValidateStockAsync();
    }

    private async Task ValidateStockAsync()
    {
        if (Lines.Count == 0)
        {
            IsValid = false;
            ValidationMessage = "يرجى إضافة المواد الخام المستهلكة أولاً";
            PostProductionCommand.NotifyCanExecuteChanged();
            return;
        }

        var consumed = Lines.Select(l => new ProductionConsumedItem
        {
            ItemId = l.ItemId,
            Quantity = l.Quantity,
            Item = new Item { Name = l.ItemName },
            Unit = new Unit { Name = l.UnitName }
        });

        var result = await _productionService.ValidateProductionItemsStockAsync(consumed);
        
        bool basicValidation = SelectedProducedItem != null && ProducedQuantity > 0 && Lines.Count > 0;
        
        IsValid = result.IsValid && basicValidation;
        
        if (!result.IsValid)
        {
            ValidationMessage = "نقص في المخزون: " + string.Join(", ", result.MissingItems.Select(m => $"{m.ItemName} (مطلوب {m.RequiredQuantity} - متوفر {m.AvailableQuantity})"));
        }
        else if (!basicValidation)
        {
            ValidationMessage = "يرجى اختيار المنتج النهائي وتحديد الكمية";
        }
        else
        {
            ValidationMessage = null;
        }

        PostProductionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveDraft))]
    private async Task SaveAsDraftAsync()
    {
        await CreateAndRedirectAsync(ProductionStatus.Draft);
    }

    private bool CanSaveDraft() => _permissionService.HasPermission(PermissionKeys.ProductionCreate);

    private bool CanPost() => IsValid && SelectedProducedItem != null && ProducedQuantity > 0 && Lines.Count > 0 && _permissionService.HasPermission(PermissionKeys.ProductionCreate);

    [RelayCommand(CanExecute = nameof(CanPost))]
    private async Task PostProductionAsync()
    {
        try
        {
            await ValidateStockAsync();
            if (!IsValid)
            {
                _messageService.ShowError("لا يمكن ترحيل الإنتاج لعدم توفر الكمية الكافية في المخزون أو نقص البيانات");
                return;
            }

            await CreateAndRedirectAsync(ProductionStatus.Draft, postImmediately: true);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Post production order"));
        }
    }

    private async Task CreateAndRedirectAsync(ProductionStatus status, bool postImmediately = false)
    {
        var activeDay = await _workingDayService.EnsureActiveWorkingDayAsync();

        var order = new ProductionOrder
        {
            ProductionNumber = $"PRD-{DateTime.UtcNow:yyyyMMddHHmm}",
            WorkingDayId = activeDay.Id,
            Status = status,
            StartedAt = DateTime.UtcNow,
            Notes = Notes,
            ConsumedItems = Lines.Select(l => new ProductionConsumedItem
            {
                ItemId = l.ItemId,
                UnitId = l.UnitId,
                Quantity = l.Quantity,
                UnitCost = l.UnitCost
            }).ToList(),
            ProducedItems = new List<ProductionProducedItem>
            {
                new ProductionProducedItem
                {
                    ItemId = SelectedProducedItem!.Id,
                    UnitId = SelectedProducedItem.BaseUnitId,
                    ExpectedProducedQty = ProducedQuantity,
                    ActualProducedQty = ProducedQuantity,
                    UnitCost = Lines.Sum(l => l.TotalCost) / ProducedQuantity
                }
            }
        };

        var created = await _productionService.CreateProductionOrderAsync(order);
        if (postImmediately)
        {
            await _productionService.PostProductionOrderAsync(created.Id);
            _messageService.ShowInfo("تم ترحيل الإنتاج بنجاح");
        }
        else
        {
            _messageService.ShowInfo("تم حفظ المسودة بنجاح");
        }
        
        _navigationService.NavigateTo<ProductionViewModel>();
    }

    [RelayCommand]
    private void Cancel() => _navigationService.NavigateTo<ProductionViewModel>();
}
