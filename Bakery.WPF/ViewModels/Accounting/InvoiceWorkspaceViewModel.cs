using System.Collections.ObjectModel;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using Microsoft.EntityFrameworkCore;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakery.Application.Security;

namespace Bakery.WPF.ViewModels;

public sealed partial class InvoiceWorkspaceViewModel : ViewModelBase
{
    private const int MaxSuggestions = 12;
    private readonly IPartyService _partyService;
    private readonly IItemService _itemService;
    private readonly ISaleInvoiceService _saleService;
    private readonly IPurchaseInvoiceService _purchaseService;
    private readonly IMessageService _messageService;
    private readonly IPermissionService _permissionService;
    private readonly List<ItemSearchEntry> _itemIndex = [];
    private CancellationTokenSource? _searchDebounce;
    private bool _isApplyingProduct;
    private ItemDto? _pendingItem;

    private readonly Bakery.Infrastructure.Data.BakeryDbContext _db;
    private readonly ISafeContext _safeContext;
    private readonly IDialogService _dialogService;
    private readonly ISafeSwitchService _safeSwitchService;
    private readonly ISafeService _safeService;
    private readonly IReceiptPrintService _receiptPrintService;

    public InvoiceWorkspaceViewModel(
        IPartyService partyService,
        IItemService itemService,
        ISaleInvoiceService saleService,
        IPurchaseInvoiceService purchaseService,
        IMessageService messageService,
        Bakery.Infrastructure.Data.BakeryDbContext db,
        IPermissionService permissionService,
        ISafeContext safeContext,
        IDialogService dialogService,
        ISafeSwitchService safeSwitchService,
        ISafeService safeService,
        IReceiptPrintService receiptPrintService)
    {
        _partyService = partyService;
        _itemService = itemService;
        _saleService = saleService;
        _purchaseService = purchaseService;
        _messageService = messageService;
        _db = db;
        _permissionService = permissionService;
        _safeContext = safeContext;
        _dialogService = dialogService;
        _safeSwitchService = safeSwitchService;
        _safeService = safeService;
        _receiptPrintService = receiptPrintService;

        Title = Loc.InvoicesTitle;
        Lines = [];
        Lines.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (InvoiceLineEditor item in e.NewItems)
                {
                    item.PropertyChanged += (_, __) => RefreshTotals();
                }
            }

            ReIndexLines();
            RefreshTotals();
        };

        Parties = [];
        Items = [];
        ProductSuggestions = [];

        var hasSales = _permissionService.HasPermission(PermissionKeys.SalesView);
        var hasPurchases = _permissionService.HasPermission(PermissionKeys.PurchasesView);

        if (hasSales && !hasPurchases)
        {
            isSale = true;
            canToggleMode = false;
        }
        else if (!hasSales && hasPurchases)
        {
            isSale = false;
            canToggleMode = false;
        }
        else
        {
            canToggleMode = true;
        }

        _ = LoadInitialDataAsync();
    }

    public event Action<string>? RequestFocus;

    [ObservableProperty] private bool isSale = true;
    [ObservableProperty] private bool canToggleMode = true;

    public bool CanChangeMode => !IsReadOnly && CanToggleMode;

    [ObservableProperty] private decimal paidAmount;
    [ObservableProperty] private PaymentType paymentType = PaymentType.Cash;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private PartyDto? selectedParty;
    [ObservableProperty] private PartyDto? originalParty;
    [ObservableProperty] private bool canEditParty = true;
    [ObservableProperty] private string readOnlyPartyName = "";
    [ObservableProperty] private bool hasInactiveParty;
    [ObservableProperty] private string partyWarning = "";
    [ObservableProperty] private string itemSearchCode = "";
    [ObservableProperty] private string pendingItemName = "انتظار البحث...";
    [ObservableProperty] private string pendingUnit = "";
    [ObservableProperty] private string pendingStockStatus = "";
    [ObservableProperty] private bool isSuggestionsOpen;
    [ObservableProperty] private ProductSuggestion? selectedSuggestion;
    [ObservableProperty] private decimal entryQuantity = 1;
    [ObservableProperty] private decimal entryPrice;
    [ObservableProperty] private int? loadedInvoiceId;
    [ObservableProperty] private int? loadedInvoiceSafeId;
    [ObservableProperty] private string invoiceNumberDisplay = "";
    [ObservableProperty] private string invoiceDateDisplay = "";
    [ObservableProperty] private bool isReadOnly;
    [ObservableProperty] private string invoiceStatusDisplay = "";

    partial void OnSelectedPartyChanged(PartyDto? value)
    {
        if (value != null && value.IsActive)
        {
            HasInactiveParty = false;
        }
    }

    partial void OnIsReadOnlyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        CancelInvoiceCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanChangeMode));
    }

    partial void OnCanToggleModeChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChangeMode));
    }

    partial void OnInvoiceStatusDisplayChanged(string value) => CancelInvoiceCommand.NotifyCanExecuteChanged();

    partial void OnLoadedInvoiceIdChanged(int? value)
    {
        PrintInvoiceCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    public ObservableCollection<InvoiceLineEditor> Lines { get; }
    public ObservableCollection<PartyDto> Parties { get; }
    public ObservableCollection<ItemDto> Items { get; }
    public ObservableCollection<ProductSuggestion> ProductSuggestions { get; }

    public decimal Total => Lines.Sum(x => x.Total);
    public decimal Remaining => Total - PaidAmount;

    partial void OnPaidAmountChanged(decimal value) => RefreshTotals();

    partial void OnIsSaleChanged(bool value)
    {
        _ = LoadPartiesAsync();
        if (_pendingItem != null) ApplyPendingItem(_pendingItem, moveFocus: false);
        SaveCommand.NotifyCanExecuteChanged();
        CancelInvoiceCommand.NotifyCanExecuteChanged();
        PrintInvoiceCommand.NotifyCanExecuteChanged();
    }

    partial void OnItemSearchCodeChanged(string value)
    {
        if (_isApplyingProduct) return;
        _ = DebouncedSearchAsync(value);
    }

    private async Task LoadInitialDataAsync()
    {
        await LoadPartiesAsync();
        var allItems = await _itemService.SearchAsync(null, null);
        foreach (var item in allItems)
        {
            Items.Add(item);
            _itemIndex.Add(new ItemSearchEntry(item));
        }

        RequestFocus?.Invoke("CodeInput");
    }

    private async Task LoadPartiesAsync()
    {
        Parties.Clear();
        var type = IsSale ? PartyType.Customer : PartyType.Supplier;
        var list = await _partyService.LookupAsync(new PartySearchRequest { Type = type, IsActive = true });
        foreach (var p in list) Parties.Add(p);
        SelectedParty = Parties.FirstOrDefault();
    }

    [RelayCommand]
    private void ProcessCodeEntry()
    {
        if (string.IsNullOrWhiteSpace(ItemSearchCode)) return;

        var item = SelectedSuggestion?.Item ?? FindBestMatch(ItemSearchCode).FirstOrDefault()?.Item;
        if (item != null)
        {
            ApplyPendingItem(item, moveFocus: true);
            return;
        }

        _pendingItem = null;
        PendingItemName = "الصنف غير موجود";
        PendingUnit = "";
        PendingStockStatus = "";
    }

    [RelayCommand]
    private void MoveSuggestion(string directionText)
    {
        if (!int.TryParse(directionText, out var direction) || ProductSuggestions.Count == 0) return;

        var currentIndex = SelectedSuggestion is null ? -1 : ProductSuggestions.IndexOf(SelectedSuggestion);
        var nextIndex = Math.Clamp(currentIndex + direction, 0, ProductSuggestions.Count - 1);
        SelectedSuggestion = ProductSuggestions[nextIndex];
        IsSuggestionsOpen = true;
    }

    [RelayCommand]
    private void AcceptSelectedProduct()
    {
        if (SelectedSuggestion?.Item is { } item)
        {
            ApplyPendingItem(item, moveFocus: true);
            return;
        }

        ProcessCodeEntry();
    }

    [RelayCommand]
    private void FocusPrice() => RequestFocus?.Invoke("PriceInput");

    [RelayCommand]
    private void AddPendingLine()
    {
        if (_pendingItem == null) return;

        AddOrMergeItem(_pendingItem, EntryQuantity, EntryPrice);
        ClearEntry();
        RequestFocus?.Invoke("CodeInput");
    }

    private void AddOrMergeItem(ItemDto item, decimal qty, decimal price)
    {
        var existing = Lines.FirstOrDefault(x => x.ItemId == item.Id);
        if (existing != null)
        {
            existing.Quantity += qty;
            existing.UnitPrice = price;
        }
        else
        {
            Lines.Add(new InvoiceLineEditor
            {
                Index = Lines.Count + 1,
                ItemId = item.Id,
                ItemName = item.Name,
                UnitId = item.BaseUnitId,
                Quantity = qty,
                UnitPrice = price
            });
        }

        RefreshTotals();
    }

    private async Task DebouncedSearchAsync(string searchText)
    {
        _searchDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _searchDebounce = cts;

        try
        {
            await Task.Delay(120, cts.Token);
            if (cts.IsCancellationRequested) return;

            var matches = FindBestMatch(searchText).Take(MaxSuggestions).ToList();
            ProductSuggestions.Clear();
            foreach (var match in matches) ProductSuggestions.Add(match);
            SelectedSuggestion = ProductSuggestions.FirstOrDefault();
            IsSuggestionsOpen = ProductSuggestions.Count > 0 && !string.IsNullOrWhiteSpace(searchText);

            if (matches.Count == 1)
            {
                ApplyPendingItem(matches[0].Item, moveFocus: true);
            }
            else if (matches.Count == 0 && !string.IsNullOrWhiteSpace(searchText))
            {
                _pendingItem = null;
                PendingItemName = "الصنف غير موجود";
                PendingUnit = "";
                PendingStockStatus = "";
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private IEnumerable<ProductSuggestion> FindBestMatch(string searchText)
    {
        var term = Normalize(searchText);
        if (string.IsNullOrWhiteSpace(term)) return [];

        return _itemIndex
            .Where(entry => entry.Matches(term))
            .OrderBy(entry => entry.Rank(term))
            .ThenBy(entry => entry.Item.Name)
            .Select(entry => new ProductSuggestion(entry.Item, IsSale));
    }

    private void ApplyPendingItem(ItemDto item, bool moveFocus)
    {
        _isApplyingProduct = true;
        try
        {
            _pendingItem = item;
            ItemSearchCode = item.Code;
            PendingItemName = item.Name;
            PendingUnit = item.BaseUnit;
            PendingStockStatus = item.CurrentStock <= 0 ? "نفذ المخزون" : "موجود";
            EntryPrice = IsSale ? (item.SalePrice > 0 ? item.SalePrice : item.PurchasePrice) : item.PurchasePrice;
            EntryQuantity = 1;
            ProductSuggestions.Clear();
            IsSuggestionsOpen = false;
            SelectedSuggestion = null;
        }
        finally
        {
            _isApplyingProduct = false;
        }

        if (moveFocus) RequestFocus?.Invoke("QtyInput");
    }

    private void ClearEntry()
    {
        _pendingItem = null;
        ItemSearchCode = "";
        PendingItemName = "انتظار البحث...";
        PendingUnit = "";
        PendingStockStatus = "";
        EntryQuantity = 1;
        EntryPrice = 0;
        ProductSuggestions.Clear();
        IsSuggestionsOpen = false;
        SelectedSuggestion = null;
    }

    private void ReIndexLines()
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            Lines[i].Index = i + 1;
        }
    }

    private void RefreshTotals()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Remaining));
    }

    [RelayCommand]
    private void RemoveLine(InvoiceLineEditor? line)
    {
        if (line != null)
        {
            Lines.Remove(line);
            RefreshTotals();
        }
    }

    [RelayCommand]
    private void NewInvoice()
    {
        Lines.Clear();
        PaidAmount = 0;
        Notes = "";
        LoadedInvoiceId = null;
        LoadedInvoiceSafeId = null;
        InvoiceNumberDisplay = "";
        InvoiceDateDisplay = "";
        IsReadOnly = false;
        InvoiceStatusDisplay = "";
        CanEditParty = true;
        HasInactiveParty = false;
        OriginalParty = null;
        PartyWarning = "";
        ClearEntry();
        RefreshTotals();
        RequestFocus?.Invoke("CodeInput");
        
        _ = LoadPartiesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSaveInvoice))]
    private async Task SaveAsync()
    {
        if (IsReadOnly) return;
        if (SelectedParty == null)
        {
            _messageService.ShowError(Loc.ErrSelectParty);
            return;
        }

        if (Lines.Count == 0)
        {
            _messageService.ShowError(Loc.ErrEmptyInvoice);
            return;
        }

        var lines = Lines.Select(x => new InvoiceLineRequest(x.ItemId, x.UnitId, x.Quantity, x.UnitPrice)).ToList();

        if (IsSale)
        {
            var req = new SaveSaleInvoiceRequest(LoadedInvoiceId, SelectedParty.Id, PaymentType, PaidAmount, Notes, lines, _safeContext.CurrentSafeId);
            var res = await _saleService.SaveDraftAsync(req);
            if (res.Succeeded && res.InvoiceId.HasValue)
            {
                var posted = await _saleService.PostAsync(res.InvoiceId.Value);
                if (posted.Succeeded)
                {
                    _messageService.ShowInfo(Loc.MsgInvoiceSaved);
                    NewInvoice();
                }
                else
                {
                    _messageService.ShowError(posted.ErrorMessage ?? Loc.ErrPostFailed);
                }
            }
            else
            {
                _messageService.ShowError(res.ErrorMessage ?? Loc.ErrSaveFailed);
            }
        }
        else
        {
            var req = new SavePurchaseInvoiceRequest(LoadedInvoiceId, SelectedParty.Id, PaymentType, PaidAmount, Notes, lines, _safeContext.CurrentSafeId);
            var res = await _purchaseService.SaveDraftAsync(req);
            if (res.Succeeded && res.InvoiceId.HasValue)
            {
                var posted = await _purchaseService.PostAsync(res.InvoiceId.Value);
                if (posted.Succeeded)
                {
                    _messageService.ShowInfo(Loc.MsgInvoiceSaved);
                    NewInvoice();
                }
                else
                {
                    _messageService.ShowError(posted.ErrorMessage ?? Loc.ErrPostFailed);
                }
            }
            else
            {
                _messageService.ShowError(res.ErrorMessage ?? Loc.ErrSaveFailed);
            }
        }
    }

    private bool CanSaveInvoice()
    {
        if (IsReadOnly) return false;
        if (LoadedInvoiceId.HasValue)
        {
            return IsSale
                ? _permissionService.HasPermission(PermissionKeys.SalesEdit)
                : _permissionService.HasPermission(PermissionKeys.PurchasesEdit);
        }
        if (IsSale)
        {
            return _permissionService.HasPermission(PermissionKeys.SalesCreate);
        }
        else
        {
            return _permissionService.HasPermission(PermissionKeys.PurchasesCreate);
        }
    }

    public bool CanCancelInvoice => IsReadOnly && LoadedInvoiceId.HasValue && InvoiceStatusDisplay == "مرحلة" && HasCancelPermission();

    private bool HasCancelPermission()
    {
        return IsSale
            ? _permissionService.HasPermission(PermissionKeys.SalesCancel)
            : _permissionService.HasPermission(PermissionKeys.PurchasesCancel);
    }

    private bool CanPrintInvoice() => LoadedInvoiceId.HasValue && (IsSale
        ? _permissionService.HasPermission(PermissionKeys.SalesPrint)
        : _permissionService.HasPermission(PermissionKeys.PurchasesPrint));

    [RelayCommand(CanExecute = nameof(CanPrintInvoice))]
    private async Task PrintInvoiceAsync()
    {
        if (!LoadedInvoiceId.HasValue) return;
        try
        {
            var document = IsSale
                ? await _saleService.GetPrintAsync(LoadedInvoiceId.Value, "Thermal")
                : await _purchaseService.GetPrintAsync(LoadedInvoiceId.Value, "Thermal");
            if (document is null)
            {
                _messageService.ShowError("تعذر العثور على الفاتورة المطلوبة للطباعة.");
                return;
            }

            await _receiptPrintService.PrintReceiptAsync(document);
        }
        catch (Exception)
        {
            _messageService.ShowError("تعذر طباعة الفاتورة. تحقق من الصلاحية وإعدادات الطابعة.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelInvoice))]
    private async Task CancelInvoiceAsync()
    {
        if (LoadedInvoiceId == null) return;

        int? originalSafeId = LoadedInvoiceSafeId;
        int? activeSafeId = _safeContext.CurrentSafeId;

        if (originalSafeId.HasValue && activeSafeId.HasValue && originalSafeId.Value != activeSafeId.Value)
        {
            string originalName = "الخزنة الأصلية";
            string activeName = "الخزنة الحالية";
            try
            {
                var originalSafe = (await _safeService.ListSafesAsync()).FirstOrDefault(s => s.Id == originalSafeId.Value);
                if (originalSafe != null) originalName = originalSafe.DisplayName;
                
                var activeSafe = _safeContext.CurrentSafe;
                if (activeSafe != null) activeName = activeSafe.DisplayName;
            }
            catch { }

            var result = await _dialogService.ShowDialogAsync<SafeMismatchDialogViewModel>(async vm =>
            {
                await vm.InitializeAsync(originalName, activeName, originalSafeId.Value);
            });

            if (result.Result != true) return; // Cancel chosen

            if (result.ViewModel.Result == MismatchResult.Cancel)
            {
                return;
            }
            else if (result.ViewModel.Result == MismatchResult.SwitchToOriginal)
            {
                var targetSafeDto = (await _safeService.ListSafesAsync()).FirstOrDefault(s => s.Id == originalSafeId.Value);
                if (targetSafeDto != null)
                {
                    await _safeSwitchService.SwitchSafeAsync(targetSafeDto);
                }
                else
                {
                    _messageService.ShowError("لا يمكن التبديل لهذه الخزنة.");
                    return;
                }
            }
        }

        var reason = await _messageService.ShowInputAsync("إلغاء الفاتورة", "يرجى إدخال سبب إلغاء الفاتورة:");
        if (string.IsNullOrWhiteSpace(reason)) return;

        try
        {
            var res = IsSale
                ? await _saleService.CancelAsync(LoadedInvoiceId.Value, reason)
                : await _purchaseService.CancelAsync(LoadedInvoiceId.Value, reason);

            if (res.Succeeded)
            {
                _messageService.ShowInfo("تم إلغاء الفاتورة بنجاح.");
                await LoadInvoiceAsync(LoadedInvoiceId.Value, IsSale);
            }
            else
            {
                _messageService.ShowError(res.ErrorMessage ?? "فشل إلغاء الفاتورة");
            }
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Cancel invoice"));
        }
    }

    public async Task LoadInvoiceAsync(int id, bool isSale)
    {
        NewInvoice();
        
        IsSale = isSale;

        if (isSale)
        {
            var inv = await _db.SaleInvoices.Include(x => x.Party).Include(x => x.Lines).ThenInclude(l => l.Item).FirstOrDefaultAsync(x => x.Id == id);
            if (inv == null) { _messageService.ShowError("الفاتورة غير موجودة."); return; }
            
            LoadedInvoiceId = inv.Id;
            LoadedInvoiceSafeId = inv.SafeId;
            InvoiceNumberDisplay = $"رقم: {inv.InvoiceNumber}";
            InvoiceDateDisplay = inv.InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
            
            if (inv.Party != null)
                OriginalParty = new PartyDto(inv.Party.Id, inv.Party.Name, inv.Party.Type, inv.Party.Phone, inv.Party.Address, inv.Party.NationalId, inv.Party.Notes, inv.Party.IsActive, 0);
            
            PaymentType = inv.PaymentType;
            PaidAmount = inv.PaidAmount;
            Notes = inv.Notes;
            IsReadOnly = inv.Status != InvoiceStatus.Draft;
            InvoiceStatusDisplay = inv.Status switch { InvoiceStatus.Draft => "مسودة", InvoiceStatus.Posted => "مرحلة", InvoiceStatus.Cancelled => "ملغاة", _ => "" };

            CanEditParty = !IsReadOnly;
            
            if (!CanEditParty)
            {
                ReadOnlyPartyName = OriginalParty?.Name ?? "";
            }
            else
            {
                await LoadPartiesAsync();
                if (OriginalParty != null && !OriginalParty.IsActive)
                {
                    SelectedParty = null;
                    HasInactiveParty = true;
                    PartyWarning = $"العميل الحالي: ({OriginalParty.Name}) - موقوف. يرجى تحديد بديل نشط.";
                }
                else
                {
                    SelectedParty = Parties.FirstOrDefault(x => x.Id == inv.PartyId);
                    HasInactiveParty = false;
                }
            }

            foreach (var line in inv.Lines)
            {
                Lines.Add(new InvoiceLineEditor
                {
                    ItemId = line.ItemId,
                    ItemName = line.Item?.Name ?? "",
                    UnitId = line.UnitId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice
                });
            }
        }
        else
        {
            var inv = await _db.PurchaseInvoices.Include(x => x.Party).Include(x => x.Lines).ThenInclude(l => l.Item).FirstOrDefaultAsync(x => x.Id == id);
            if (inv == null) { _messageService.ShowError("الفاتورة غير موجودة."); return; }
            
            LoadedInvoiceId = inv.Id;
            LoadedInvoiceSafeId = inv.SafeId;
            InvoiceNumberDisplay = $"رقم: {inv.InvoiceNumber}";
            InvoiceDateDisplay = inv.InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
            
            if (inv.Party != null)
                OriginalParty = new PartyDto(inv.Party.Id, inv.Party.Name, inv.Party.Type, inv.Party.Phone, inv.Party.Address, inv.Party.NationalId, inv.Party.Notes, inv.Party.IsActive, 0);
            
            PaymentType = inv.PaymentType;
            PaidAmount = inv.PaidAmount;
            Notes = inv.Notes;
            IsReadOnly = inv.Status != InvoiceStatus.Draft;
            InvoiceStatusDisplay = inv.Status switch { InvoiceStatus.Draft => "مسودة", InvoiceStatus.Posted => "مرحلة", InvoiceStatus.Cancelled => "ملغاة", _ => "" };

            CanEditParty = !IsReadOnly;
            
            if (!CanEditParty)
            {
                ReadOnlyPartyName = OriginalParty?.Name ?? "";
            }
            else
            {
                await LoadPartiesAsync();
                if (OriginalParty != null && !OriginalParty.IsActive)
                {
                    SelectedParty = null;
                    HasInactiveParty = true;
                    PartyWarning = $"المورد الحالي: ({OriginalParty.Name}) - موقوف. يرجى تحديد بديل نشط.";
                }
                else
                {
                    SelectedParty = Parties.FirstOrDefault(x => x.Id == inv.PartyId);
                    HasInactiveParty = false;
                }
            }

            foreach (var line in inv.Lines)
            {
                Lines.Add(new InvoiceLineEditor
                {
                    ItemId = line.ItemId,
                    ItemName = line.Item?.Name ?? "",
                    UnitId = line.UnitId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice
                });
            }
        }
        
        RefreshTotals();
    }

    private static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();

    public sealed record ProductSuggestion(ItemDto Item, bool IsSale)
    {
        public string Code => Item.Code;
        public string Name => Item.Name;
        public string Unit => Item.BaseUnit;
        public string StockStatus => Item.CurrentStock <= 0 ? "نفذ المخزون" : "موجود";
        public decimal Price => IsSale ? (Item.SalePrice > 0 ? Item.SalePrice : Item.PurchasePrice) : Item.PurchasePrice;
    }

    private sealed class ItemSearchEntry
    {
        public ItemSearchEntry(ItemDto item)
        {
            Item = item;
            Code = Normalize(item.Code);
            Barcode = Normalize(item.Barcode);
            Name = Normalize(item.Name);
        }

        public ItemDto Item { get; }
        private string Code { get; }
        private string Barcode { get; }
        private string Name { get; }

        public bool Matches(string term) => Code.Contains(term) || Barcode.Contains(term) || Name.Contains(term);

        public int Rank(string term)
        {
            if (Code == term || Barcode == term) return 0;
            if (Code.StartsWith(term) || Barcode.StartsWith(term)) return 1;
            if (Name.StartsWith(term)) return 2;
            if (Name.Contains(term)) return 3;
            return 4;
        }
    }
}
