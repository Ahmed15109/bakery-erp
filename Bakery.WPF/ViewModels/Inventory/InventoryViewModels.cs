using System.Collections.ObjectModel;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakery.Application.Security;

namespace Bakery.WPF.ViewModels;

public sealed partial class ItemsViewModel : ViewModelBase
{
    private readonly IItemService _itemService;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IPermissionService _permissionService;
    private readonly INavigationService _navigationService;
    private CancellationTokenSource? _searchCts;

    public ItemsViewModel(IItemService itemService, IMessageService messageService, IDialogService dialogService, IPermissionService permissionService, INavigationService navigationService)
    {
        _itemService = itemService;
        _messageService = messageService;
        _dialogService = dialogService;
        _permissionService = permissionService;
        _navigationService = navigationService;
        Title = Loc.Items;
        Items = [];
        _ = RefreshAsync();
    }

    public ObservableCollection<ItemDto> Items { get; }

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private int selectedTypeIndex = 0;
    [ObservableProperty] private ItemDto? selectedItem;

    [ObservableProperty] private int totalItems;
    [ObservableProperty] private int lowStockCount;
    [ObservableProperty] private int rawMaterialsCount;
    [ObservableProperty] private int finishedProductsCount;
    [ObservableProperty] private decimal totalInventoryValue;

    [RelayCommand] public Task RefreshAsync() => LoadAsync();
    
    [RelayCommand] 
    private void ClearSearch() => SearchText = "";

    [RelayCommand]
    private void Back() => _navigationService.NavigateTo<InventoryHomeViewModel>();

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Task.Delay(250, token).ContinueWith(t =>
        {
            if (!t.IsCanceled) System.Windows.Application.Current.Dispatcher.Invoke(() => _ = LoadAsync());
        }, token);
    }

    partial void OnSelectedTypeIndexChanged(int value) => _ = LoadAsync();

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        var result = await _dialogService.ShowDialogAsync<ItemFormDialogViewModel>();
        if (result.Result == true) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAsync(ItemDto? item)
    {
        var target = item ?? SelectedItem;
        if (target is null) return;
        var result = await _dialogService.ShowDialogAsync<ItemFormDialogViewModel>(async vm =>
        {
            await vm.LoadAsync(target.Id);
        });
        if (result.Result == true) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(ItemDto? item)
    {
        var target = item ?? SelectedItem;
        if (target is null) return;
        
        if (!_messageService.Confirm(Loc.ConfirmDelete)) return;

        var result = await _itemService.SoftDeleteAsync(target.Id);
        if (!result.Succeeded) _messageService.ShowError(result.ErrorMessage ?? Loc.ErrCannotDeleteItem);
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ToggleActiveAsync(ItemDto? item)
    {
        var target = item ?? SelectedItem;
        if (target is null) return;
        await _itemService.SetActiveAsync(target.Id, !target.IsActive);
        await RefreshAsync();
    }

    private bool CanAdd() => _permissionService.HasPermission(PermissionKeys.ProductsAdd);
    private bool CanEdit() => _permissionService.HasPermission(PermissionKeys.ProductsEdit);
    private bool CanDelete() => _permissionService.HasPermission(PermissionKeys.ProductsDelete);

    private async Task LoadAsync()
    {
        ItemType? type = SelectedTypeIndex switch
        {
            1 => ItemType.RawMaterial,
            2 => ItemType.FinishedProduct,
            3 => ItemType.Fuel,
            4 => ItemType.Packaging,
            _ => null
        };

        var allItems = await _itemService.SearchAsync(SearchText, type);
        
        Items.Clear();
        foreach (var item in allItems) Items.Add(item);

        TotalItems = allItems.Count;
        LowStockCount = allItems.Count(x => x.CurrentStock <= x.MinStockLevel && x.IsActive);
        RawMaterialsCount = allItems.Count(x => x.Type == ItemType.RawMaterial);
        FinishedProductsCount = allItems.Count(x => x.Type == ItemType.FinishedProduct);
        TotalInventoryValue = allItems.Sum(x => x.CurrentStock * x.PurchasePrice);
    }
}

public sealed partial class ItemFormDialogViewModel : ViewModelBase
{
    private readonly IItemService _itemService;
    private readonly IUnitService _unitService;
    private readonly IMessageService _messageService;
    private readonly IValidationService _validationService;
    private readonly IExceptionTranslator _exceptionTranslator;
    private CancellationTokenSource? _validationCts;

    public ItemFormDialogViewModel(IItemService itemService, IUnitService unitService, IMessageService messageService, IValidationService validationService, IExceptionTranslator exceptionTranslator)
    {
        _itemService = itemService;
        _unitService = unitService;
        _messageService = messageService;
        _validationService = validationService;
        _exceptionTranslator = exceptionTranslator;
        Title = Loc.ItemFormTitle;
        Units = [];
        ItemTypes = Enum.GetValues<ItemType>();
        IsActive = true;
        _ = LoadUnitsAsync();
    }

    public ObservableCollection<UnitDto> Units { get; }
    public IReadOnlyList<ItemType> ItemTypes { get; }
    [ObservableProperty] private int? itemId;
    [ObservableProperty] private string code = string.Empty;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string? barcode;
    [ObservableProperty] private ItemType selectedType;
    [ObservableProperty] private int selectedUnitId;
    [ObservableProperty] private decimal purchasePrice;
    [ObservableProperty] private decimal salePrice;
    [ObservableProperty] private decimal minStockLevel;
    [ObservableProperty] private decimal reorderLevel;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private string? notes;

    [ObservableProperty] private bool? isCodeValid;
    [ObservableProperty] private string codeValidationMessage = string.Empty;
    [ObservableProperty] private bool? isBarcodeValid;
    [ObservableProperty] private string barcodeValidationMessage = string.Empty;

    public bool CanSave 
    {
        get
        {
            bool hasName = !string.IsNullOrWhiteSpace(Name);
            bool hasCode = !string.IsNullOrWhiteSpace(Code);
            bool hasUnit = SelectedUnitId != 0;
            bool codeValid = IsCodeValid != false;
            bool barcodeValid = IsBarcodeValid != false;
            
            bool canSave = hasName && hasCode && hasUnit && codeValid && barcodeValid;
            
            System.Diagnostics.Debug.WriteLine($"[ItemForm] CanSave: {canSave} | Name:{hasName} | Code:{hasCode} | Unit:{hasUnit} | CodeValid:{codeValid} | BarcodeValid:{barcodeValid}");
            
            return canSave;
        }
    }

    public event EventHandler<bool>? RequestClose;

    partial void OnNameChanged(string value) => RefreshSaveCommand();
    partial void OnCodeChanged(string value)
    {
        _ = ValidateCodeAsync(value);
        RefreshSaveCommand();
    }
    partial void OnBarcodeChanged(string? value)
    {
        _ = ValidateBarcodeAsync(value);
        RefreshSaveCommand();
    }
    partial void OnSelectedUnitIdChanged(int value) => RefreshSaveCommand();
    partial void OnSelectedTypeChanged(ItemType value) => RefreshSaveCommand();

    private void RefreshSaveCommand()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private async Task ValidateCodeAsync(string code)
    {
        _validationCts?.Cancel();
        _validationCts = new CancellationTokenSource();
        var token = _validationCts.Token;

        try
        {
            await Task.Delay(300, token);
            if (string.IsNullOrWhiteSpace(code))
            {
                IsCodeValid = false;
                CodeValidationMessage = "كود الصنف مطلوب";
            }
            else
            {
                var used = await _validationService.IsItemCodeUsedAsync(code, ItemId);
                IsCodeValid = !used;
                CodeValidationMessage = used ? "كود الصنف مستخدم بالفعل" : "✅ الكود متاح";
            }
        }
        catch (OperationCanceledException) { }
        finally { RefreshSaveCommand(); }
    }

    private async Task ValidateBarcodeAsync(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            IsBarcodeValid = true;
            BarcodeValidationMessage = string.Empty;
            OnPropertyChanged(nameof(CanSave));
            return;
        }

        try
        {
            await Task.Delay(300);
            var used = await _validationService.IsBarcodeUsedAsync(barcode, ItemId);
            IsBarcodeValid = !used;
            BarcodeValidationMessage = used ? "الباركود مستخدم بالفعل" : "✅ الباركود متاح";
        }
        catch { }
        finally { RefreshSaveCommand(); }
    }

    public async Task LoadAsync(int id)
    {
        await LoadUnitsAsync();
        var item = await _itemService.GetByIdAsync(id);
        if (item is null) return;

        IsCodeValid = true; 
        IsBarcodeValid = true;

        ItemId = item.Id; 
        Code = item.Code; 
        Name = item.Name; 
        Barcode = item.Barcode; 
        SelectedType = item.Type; 
        SelectedUnitId = item.BaseUnitId; 
        PurchasePrice = item.PurchasePrice; 
        SalePrice = item.SalePrice; 
        MinStockLevel = item.MinStockLevel; 
        ReorderLevel = item.ReorderLevel; 
        IsActive = item.IsActive; 
        Notes = item.Notes;

        RefreshSaveCommand();
    }

    private async Task LoadUnitsAsync()
    {
        if (Units.Count > 0) return;
        foreach (var unit in await _unitService.ListAsync()) Units.Add(unit);
        if (SelectedUnitId == 0) SelectedUnitId = Units.FirstOrDefault()?.Id ?? 0;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        try
        {
            var result = await _itemService.SaveAsync(new SaveItemRequest(ItemId, Code, Name, Barcode, SelectedType, SelectedUnitId, PurchasePrice, SalePrice, MinStockLevel, ReorderLevel, IsActive, Notes));
            if (!result.Succeeded) { _messageService.ShowError(result.ErrorMessage ?? Loc.ErrSaveFailed); return; }
            RequestClose?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand] private void Cancel() => RequestClose?.Invoke(this, false);
}

public sealed partial class UnitsViewModel : ViewModelBase
{
    private readonly IUnitService _unitService;
    private readonly IMessageService _messageService;
    private readonly IExceptionTranslator _exceptionTranslator;
    private readonly IPermissionService _permissionService;
    private readonly INavigationService _navigationService;
    public UnitsViewModel(IUnitService unitService, IMessageService messageService, IExceptionTranslator exceptionTranslator, IPermissionService permissionService, INavigationService navigationService)
    {
        _unitService = unitService; _messageService = messageService; _exceptionTranslator = exceptionTranslator; _permissionService = permissionService; _navigationService = navigationService; Title = Loc.Units; Units = []; IsActive = true; _ = RefreshAsync();
    }
    public ObservableCollection<UnitDto> Units { get; }
    [ObservableProperty] private UnitDto? selectedUnit;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string symbol = string.Empty;
    [ObservableProperty] private bool isActive;
    [RelayCommand] private async Task RefreshAsync() { Units.Clear(); foreach (var unit in await _unitService.ListAsync()) Units.Add(unit); }
    [RelayCommand] private void Back() => _navigationService.NavigateTo<InventoryHomeViewModel>();
    [RelayCommand] private void Edit() { if (SelectedUnit is null) return; Name = SelectedUnit.Name; Symbol = SelectedUnit.Symbol; IsActive = SelectedUnit.IsActive; }
    [RelayCommand(CanExecute = nameof(CanSave))] private async Task SaveAsync() { 
        try {
            var result = await _unitService.SaveAsync(new SaveUnitRequest(SelectedUnit?.Id, Name, Symbol, IsActive)); 
            if (!result.Succeeded) _messageService.ShowError(result.ErrorMessage ?? Loc.ErrSaveFailed); 
            Name = ""; Symbol = ""; SelectedUnit = null; await RefreshAsync(); 
        } catch (Exception ex) {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(UnitDto? unit)
    {
        if (unit is null) return;
        if (!_messageService.Confirm("هل أنت متأكد من حذف الوحدة؟")) return;

        var result = await _unitService.DeleteAsync(unit.Id);
        if (!result.Succeeded)
        {
            _messageService.ShowError(result.ErrorMessage ?? "لا يمكن حذف الوحدة لأنها مستخدمة في النظام");
            return;
        }

        if (SelectedUnit?.Id == unit.Id)
        {
            SelectedUnit = null;
            Name = "";
            Symbol = "";
            IsActive = true;
        }

        await RefreshAsync();
    }

    private bool CanSave() => _permissionService.HasPermission(PermissionKeys.ProductsEdit);
    private bool CanDelete() => _permissionService.HasPermission(PermissionKeys.ProductsDelete);
}

public sealed partial class InventoryViewModel : ViewModelBase
{
    private readonly IStockCalculationService _stockService; 
    private readonly IDialogService _dialogService; 
    private readonly INavigationService _navigationService;
    private readonly IPermissionService _permissionService;
    
    public InventoryViewModel(IStockCalculationService stockService, IDialogService dialogService, INavigationService navigationService, IPermissionService permissionService) 
    { 
        _stockService = stockService; 
        _dialogService = dialogService; 
        _navigationService = navigationService; 
        _permissionService = permissionService;
        Title = Loc.Inventory; 
        Stock = []; 
        _ = RefreshAsync(); 
    }
    
    public ObservableCollection<StockItemDto> Stock { get; }
    [ObservableProperty] private decimal valuation;
    [RelayCommand] 
    public async Task RefreshAsync() 
    { 
        Stock.Clear(); 
        var hasAccess = _permissionService.HasPermission(PermissionKeys.InventoryView) || _permissionService.HasPermission(PermissionKeys.ReportsInventory);
        if (hasAccess)
        {
            foreach (var item in await _stockService.GetCurrentStockAsync()) Stock.Add(item); 
            Valuation = await _stockService.GetStockValuationAsync(); 
        }
        else
        {
            Valuation = 0;
        }
    }
    [RelayCommand] private async Task AdjustAsync() { var result = await _dialogService.ShowDialogAsync<InventoryAdjustmentDialogViewModel>(); if (result.Result == true) await RefreshAsync(); }
    [RelayCommand] private void Items() => _navigationService.NavigateTo<ItemsViewModel>();
    [RelayCommand] private void Units() => _navigationService.NavigateTo<UnitsViewModel>();
    [RelayCommand] private void StockCount() => _navigationService.NavigateTo<StockCountViewModel>();
    [RelayCommand] private void Movements() => _navigationService.NavigateTo<InventoryMovementsViewModel>();
    [RelayCommand] private void Back() => _navigationService.NavigateTo<InventoryHomeViewModel>();
}

public sealed partial class InventoryAdjustmentDialogViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService; private readonly IItemService _itemService; private readonly IUnitService _unitService; private readonly IMessageService _messageService; private readonly IExceptionTranslator _exceptionTranslator;
    private int _unitLoadVersion;
    public InventoryAdjustmentDialogViewModel(IInventoryService inventoryService, IItemService itemService, IUnitService unitService, IMessageService messageService, IExceptionTranslator exceptionTranslator) { _inventoryService = inventoryService; _itemService = itemService; _unitService = unitService; _messageService = messageService; _exceptionTranslator = exceptionTranslator; Items = []; Units = []; IsIncrease = true; _ = LoadAsync(); }
    public ObservableCollection<ItemDto> Items { get; }
    public ObservableCollection<ItemUnitDto> Units { get; }
    [ObservableProperty] private int selectedItemId; [ObservableProperty] private int selectedUnitId; [ObservableProperty] private decimal quantity; [ObservableProperty] private bool isIncrease; [ObservableProperty] private string reason = string.Empty;
    public event EventHandler<bool>? RequestClose;
    private async Task LoadAsync() { foreach (var item in await _itemService.SearchAsync(null, null)) Items.Add(item); SelectedItemId = Items.FirstOrDefault()?.Id ?? 0; }
    partial void OnSelectedItemIdChanged(int value) => _ = LoadUnitsAsync(value);
    private async Task LoadUnitsAsync(int itemId)
    {
        var version = Interlocked.Increment(ref _unitLoadVersion);
        var units = itemId == 0 ? [] : await _unitService.GetItemUnitsAsync(itemId);
        if (version != _unitLoadVersion) return;
        Units.Clear();
        foreach (var unit in units) Units.Add(unit);
        SelectedUnitId = Units.FirstOrDefault(unit => unit.IsDefaultUnit)?.UnitId
            ?? Units.FirstOrDefault()?.UnitId
            ?? 0;
    }
    [RelayCommand] private async Task SaveAsync() { 
        try {
            var result = await _inventoryService.AdjustStockAsync(new InventoryAdjustmentRequest(SelectedItemId, SelectedUnitId, Quantity, IsIncrease, Reason)); 
            if (!result.Succeeded) { _messageService.ShowError(result.ErrorMessage ?? Loc.ErrSaveFailed); return; } 
            RequestClose?.Invoke(this, true); 
        } catch (Exception ex) {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }
    [RelayCommand] private void Cancel() => RequestClose?.Invoke(this, false);
}
