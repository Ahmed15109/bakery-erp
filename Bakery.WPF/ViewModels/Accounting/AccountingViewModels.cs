using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using Bakery.WPF.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Bakery.Application.Security;

namespace Bakery.WPF.ViewModels;

public sealed partial class PartiesViewModel : ViewModelBase
{
    private readonly IPartyService _service;
    private readonly INavigationService _nav;
    private readonly IMessageService _messages;
    private readonly IValidationService _validationService;
    private readonly IExceptionTranslator _exceptionTranslator;
    private readonly IPartyLookupService _partyLookupService;
    private readonly IEmployeeService _employeeService;
    private readonly IDialogService _dialogService;
    private readonly IPermissionService _permissionService;

    public PartiesViewModel(
        IPartyService service, 
        INavigationService nav, 
        IMessageService messages, 
        IValidationService validationService, 
        IExceptionTranslator exceptionTranslator,
        IPartyLookupService partyLookupService,
        IEmployeeService employeeService,
        IDialogService dialogService,
        IPermissionService permissionService)
    {
        _service = service;
        _nav = nav;
        _messages = messages;
        _validationService = validationService;
        _exceptionTranslator = exceptionTranslator;
        _partyLookupService = partyLookupService;
        _employeeService = employeeService;
        _dialogService = dialogService;
        _permissionService = permissionService;
        Title = Loc.Parties;
        Parties = [];
        RecentParties = [];
        _ = RefreshAsync();
    }

    public ObservableCollection<PartyDto> Parties { get; }
    public ObservableCollection<PartyDto> RecentParties { get; }

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private PartyStatsDto? stats;
    [ObservableProperty] private PartyDto? selectedParty;
    [ObservableProperty] private int selectedTabIndex = 0; // 0: All, 1: Customers, 2: Suppliers, 3: Employees

    private CancellationTokenSource? _searchCts;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Stats = await _service.GetStatsAsync();
        
        PartyType? typeFilter = SelectedTabIndex switch
        {
            1 => PartyType.Customer,
            2 => PartyType.Supplier,
            3 => PartyType.Employee,
            _ => null
        };

        Parties.Clear();
        var results = await _service.SearchAsync(new PartySearchRequest { Search = SearchText, Type = typeFilter, IncludeDeleted = false });
        foreach (var p in results) Parties.Add(p);

        // Update Recent (last 5)
        RecentParties.Clear();
        foreach (var p in results.Take(5)) RecentParties.Add(p);
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Task.Delay(300, token).ContinueWith(t =>
        {
            if (!t.IsCanceled) System.Windows.Application.Current.Dispatcher.Invoke(() => _ = RefreshAsync());
        }, token);
    }

    partial void OnSelectedTabIndexChanged(int value) => _ = RefreshAsync();

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddPartyAsync()
    {
        var vm = new PartyFormViewModel(_validationService);
        bool tryAgain = true;
        while (tryAgain)
        {
            tryAgain = false;
            var result = await DialogHost.Show(new PartyFormDialog { DataContext = vm }, "RootDialog");

            if (result is bool b && b)
            {
                var dupCheck = await _service.CheckNameDuplicatesAsync(vm.Name);
                if (dupCheck.HasDuplicates)
                {
                    if (!_messages.Confirm(dupCheck.WarningMessage))
                    {
                        tryAgain = true;
                        continue;
                    }
                }

                try
                {
                    var saveResult = await _service.SaveAsync(vm.ToRequest());
                    if (saveResult.Succeeded) await RefreshAsync();
                    else _messages.ShowError(saveResult.ErrorMessage ?? "فشل حفظ البيانات");
                }
                catch (Exception ex)
                {
                    _messages.ShowError(_exceptionTranslator.Translate(ex));
                }
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditPartyAsync(PartyDto party)
    {
        if (party == null) return;
        var vm = new PartyFormViewModel(party, _validationService);
        bool tryAgain = true;
        while (tryAgain)
        {
            tryAgain = false;
            var result = await DialogHost.Show(new PartyFormDialog { DataContext = vm }, "RootDialog");

            if (result is bool b && b)
            {
                var dupCheck = await _service.CheckNameDuplicatesAsync(vm.Name, party.Id);
                if (dupCheck.HasDuplicates)
                {
                    if (!_messages.Confirm(dupCheck.WarningMessage))
                    {
                        tryAgain = true;
                        continue;
                    }
                }

                try
                {
                    var saveResult = await _service.SaveAsync(vm.ToRequest());
                    if (saveResult.Succeeded) await RefreshAsync();
                    else _messages.ShowError(saveResult.ErrorMessage ?? "فشل حفظ البيانات");
                }
                catch (Exception ex)
                {
                    _messages.ShowError(_exceptionTranslator.Translate(ex));
                }
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeletePartyAsync(PartyDto party)
    {
        if (party == null) return;
        
        if (!_messages.Confirm($"هل أنت متأكد من حذف {party.Name}؟")) return;

        var result = await _service.DeleteAsync(party.Id);
        if (!result.Succeeded) _messages.ShowError(result.ErrorMessage ?? "فشل حذف الطرف");
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ToggleActiveAsync(PartyDto party)
    {
        if (party == null) return;
        await _service.SetActiveAsync(party.Id, !party.IsActive);
        await RefreshAsync();
    }

    private bool CanAdd() => _permissionService.HasPermission(PermissionKeys.CustomersAdd);
    private bool CanEdit() => _permissionService.HasPermission(PermissionKeys.CustomersEdit);
    private bool CanDelete() => _permissionService.HasPermission(PermissionKeys.CustomersDelete);

    [RelayCommand(CanExecute = nameof(CanShowStatement))]
    private async Task ShowStatementAsync(PartyDto party)
    {
        if (party == null) return;

        if (party.Type == PartyType.Employee)
        {
            var routingInfo = await _partyLookupService.GetPartyRoutingInfoAsync(party.Id);
            if (routingInfo.EmployeeId.HasValue)
            {
                var employee = await _employeeService.GetEmployeeByIdAsync(routingInfo.EmployeeId.Value);
                if (employee != null)
                {
                    await _dialogService.ShowDialogAsync<EmployeeLedgerViewModel>(async vm =>
                        await vm.InitializeAsync(employee));
                    return;
                }
            }
        }

        PartyStatementViewModel.SelectedPartyId = party.Id;
        _nav.NavigateTo<PartyStatementViewModel>();
    }

    private bool CanShowStatement(PartyDto? party)
    {
        if (party is null) return false;
        return party.Type switch
        {
            PartyType.Customer => _permissionService.HasPermission(PermissionKeys.CustomersView),
            PartyType.Supplier => _permissionService.HasPermission(PermissionKeys.PurchasesView),
            PartyType.Employee => _permissionService.HasPermission(PermissionKeys.EmployeesViewSalary),
            PartyType.Mixed => _permissionService.HasPermission(PermissionKeys.CustomersView) &&
                               _permissionService.HasPermission(PermissionKeys.PurchasesView),
            _ => false
        };
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SearchText = "";
        SelectedTabIndex = 0;
    }
}

public sealed partial class PartyStatementViewModel : ViewModelBase
{
    public static int SelectedPartyId { get; set; }
    private readonly IPartyService _partyService;
    private readonly IStatementService _statementService;

    [ObservableProperty] private PartySummaryDto? summary;
    [ObservableProperty] private bool isCustomer;
    [ObservableProperty] private string increaseHeader = "مبيعات";
    [ObservableProperty] private string decreaseHeader = "مدفوعات";
    [ObservableProperty] private bool canPay;

    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    public PartyStatementViewModel(IPartyService partyService, IStatementService statementService, INavigationService navigationService, IDialogService dialogService) 
    { 
        _partyService = partyService;
        _statementService = statementService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        Title = Loc.StatementTitle; 
        Lines = []; 
        _ = RefreshAsync(); 
    }

    public ObservableCollection<PartyStatementLineDto> Lines { get; }
    
    [ObservableProperty] private PartyStatementLineDto? selectedLine;

    [RelayCommand]
    private void OpenSelectedInvoice()
    {
        var value = SelectedLine;
        if (value == null || value.ReferenceId == null) return;
        
        if (value.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice || value.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice)
        {
            var vm = _navigationService.NavigateTo<InvoiceWorkspaceViewModel>();
            _ = vm.LoadInvoiceAsync(value.ReferenceId.Value, value.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice);
        }
    }

    [RelayCommand] 
    private async Task RefreshAsync() 
    {
        try
        {
            Debug.WriteLine($"[PartyStatement] RefreshAsync START — SelectedPartyId={SelectedPartyId}");

            Summary = await _partyService.GetPartySummaryAsync(SelectedPartyId);
            Debug.WriteLine($"[PartyStatement] Summary loaded — Type={Summary?.Type}, Balance={Summary?.CurrentBalance}");

            if (Summary != null)
            {
                IsCustomer = Summary.Type == PartyType.Customer;
                if (Summary.Type == PartyType.Mixed)
                {
                    IncreaseHeader = "المبيعات (+) / المشتريات (+)";
                    DecreaseHeader = "المحصل (-) / المسدد (-)";
                }
                else if (Summary.Type == PartyType.Employee)
                {
                    IncreaseHeader = "الاستحقاقات";
                    DecreaseHeader = "المصروف";
                }
                else
                {
                    IncreaseHeader = IsCustomer ? "المبيعات" : "المشتريات";
                    DecreaseHeader = IsCustomer ? "المحصل" : "المسدد";
                }
                CanPay = Math.Abs(Summary.CurrentBalance) > 0;
            }

            Debug.WriteLine($"[PartyStatement] Calling GetStatementAsync({SelectedPartyId})");
            Lines.Clear(); 
            var data = await _statementService.GetStatementAsync(SelectedPartyId);
            Debug.WriteLine($"[PartyStatement] GetStatementAsync returned {data.Count} rows");
            foreach (var line in data) Lines.Add(line);
            Debug.WriteLine($"[PartyStatement] Lines.Count={Lines.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(Bakery.Shared.Security.SensitiveDataRedactor.Redact(
                $"[PartyStatement] EXCEPTION in RefreshAsync: {ex.GetType().Name}: {ex.Message}"));
            Debug.WriteLine(Bakery.Shared.Security.SensitiveDataRedactor.Redact(ex.StackTrace));
            throw;
        }
    }

    [RelayCommand]
    private async Task PayAsync()
    {
        if (Summary == null) return;
        
        var result = await _dialogService.ShowDialogAsync<PartyPaymentDialogViewModel>(async vm =>
        {
            await vm.InitializeAsync(SelectedPartyId, Summary);
        });
        
        if (result.Result == true)
        {
            await RefreshAsync();
        }
    }
}

public sealed partial class SalesViewModel : InvoiceListViewModel
{
    private readonly Task _initialLoad;
    public SalesViewModel(ISaleInvoiceService service, IDialogService dialogService) : base(dialogService)
    {
        SaleService = service;
        Title = Loc.Sales;
        _initialLoad = RefreshAsync();
    }
    private ISaleInvoiceService SaleService { get; }
    [ObservableProperty] private InvoiceStatus? statusFilter;
    protected override async Task<IReadOnlyList<InvoiceDto>> LoadInvoicesAsync() => await SaleService.ListAsync(StatusFilter);
    protected override WindowKind Kind => WindowKind.Sale;

    public async Task ShowBlockingInvoiceAsync(int invoiceId)
    {
        await _initialLoad;
        StatusFilter = null;
        await RefreshAsync();
        SelectedInvoice = Invoices.SingleOrDefault(invoice => invoice.Id == invoiceId);
    }
}

public sealed partial class PurchasesViewModel : InvoiceListViewModel
{
    private readonly Task _initialLoad;

    public PurchasesViewModel(IPurchaseInvoiceService service, IDialogService dialogService) : base(dialogService)
    {
        PurchaseService = service;
        Title = Loc.Purchases;
        _initialLoad = RefreshAsync();
    }

    private IPurchaseInvoiceService PurchaseService { get; }
    [ObservableProperty] private InvoiceStatus? statusFilter;
    protected override async Task<IReadOnlyList<InvoiceDto>> LoadInvoicesAsync() => await PurchaseService.ListAsync(StatusFilter);
    protected override WindowKind Kind => WindowKind.Purchase;

    public async Task ShowBlockingDraftAsync(int invoiceId)
    {
        await _initialLoad;
        StatusFilter = InvoiceStatus.Draft;
        await RefreshAsync();
        SelectedInvoice = Invoices.SingleOrDefault(invoice => invoice.Id == invoiceId);
    }

    public async Task ShowBlockingInvoiceAsync(int invoiceId)
    {
        await _initialLoad;
        StatusFilter = null;
        await RefreshAsync();
        SelectedInvoice = Invoices.SingleOrDefault(invoice => invoice.Id == invoiceId);
    }
}

public abstract partial class InvoiceListViewModel : ViewModelBase
{
    protected readonly IDialogService DialogService;
    protected InvoiceListViewModel(IDialogService dialogService) { DialogService = dialogService; Invoices = []; }
    public ObservableCollection<InvoiceDto> Invoices { get; }
    [ObservableProperty] private InvoiceDto? selectedInvoice;
    protected enum WindowKind { Sale, Purchase }
    protected abstract WindowKind Kind { get; }
    protected abstract Task<IReadOnlyList<InvoiceDto>> LoadInvoicesAsync();
    [RelayCommand] public async Task RefreshAsync() { Invoices.Clear(); foreach (var i in await LoadInvoicesAsync()) Invoices.Add(i); }
    [RelayCommand] 
    private async Task NewAsync() 
    { 
        bool? result;
        if (Kind == WindowKind.Sale)
        {
            var dialogResult = await DialogService.ShowDialogAsync<SaleInvoiceDialogViewModel>();
            result = dialogResult.Result;
        }
        else
        {
            var dialogResult = await DialogService.ShowDialogAsync<PurchaseInvoiceDialogViewModel>();
            result = dialogResult.Result;
        }
        if (result == true) await RefreshAsync(); 
    }
}

public sealed partial class SaleInvoiceDialogViewModel : InvoiceDialogViewModel
{
    private readonly ISaleInvoiceService _service;
    public SaleInvoiceDialogViewModel(ISaleInvoiceService service, IPartyService parties, IItemService items, IUnitService units, IMessageService messages, IRecoveryService recovery, ISafeService safes, ISafeContext safeContext) 
        : base(parties, items, units, messages, recovery, safes, safeContext, PartyType.Customer, "SaleDraft") { _service = service; Title = Loc.SaleInvoiceTitle; }
    protected override async Task<(bool Succeeded, string? ErrorMessage, int? InvoiceId)> SaveCoreAsync() => await _service.SaveDraftAsync(new SaveSaleInvoiceRequest(null, SelectedPartyId, PaymentType, PaidAmount, Notes, Lines.Select(x => new InvoiceLineRequest(x.ItemId, x.UnitId, x.Quantity, x.UnitPrice)).ToList(), _safeContext.CurrentSafeId));
    protected override Task<(bool Succeeded, string? ErrorMessage)> PostCoreAsync(int id) => _service.PostAsync(id);
}

public sealed partial class PurchaseInvoiceDialogViewModel : InvoiceDialogViewModel
{
    private readonly IPurchaseInvoiceService _service;
    public PurchaseInvoiceDialogViewModel(IPurchaseInvoiceService service, IPartyService parties, IItemService items, IUnitService units, IMessageService messages, IRecoveryService recovery, ISafeService safes, ISafeContext safeContext) 
        : base(parties, items, units, messages, recovery, safes, safeContext, PartyType.Supplier, "PurchaseDraft") { _service = service; Title = Loc.PurchaseInvoiceTitle; }
    protected override async Task<(bool Succeeded, string? ErrorMessage, int? InvoiceId)> SaveCoreAsync() => await _service.SaveDraftAsync(new SavePurchaseInvoiceRequest(null, SelectedPartyId, PaymentType, PaidAmount, Notes, Lines.Select(x => new InvoiceLineRequest(x.ItemId, x.UnitId, x.Quantity, x.UnitPrice)).ToList(), _safeContext.CurrentSafeId));
    protected override Task<(bool Succeeded, string? ErrorMessage)> PostCoreAsync(int id) => _service.PostAsync(id);
}

public sealed partial class InvoiceLineEditor : ObservableObject
{
    [ObservableProperty] private int index;
    [ObservableProperty] private int itemId; 
    [ObservableProperty] private string itemName = ""; 
    [ObservableProperty] private int unitId; 
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Total))] private decimal quantity = 1; 
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Total))] private decimal unitPrice;
    public decimal Total => Quantity * UnitPrice;
}

public abstract partial class InvoiceDialogViewModel : ViewModelBase
{
    private readonly IPartyService _parties; private readonly IItemService _items; private readonly IUnitService _units; private readonly IMessageService _messages; private readonly PartyType _partyType; private readonly IRecoveryService _recovery; private readonly ISafeService _safes; protected readonly ISafeContext _safeContext; private readonly string _draftKey;
    protected InvoiceDialogViewModel(IPartyService parties, IItemService items, IUnitService units, IMessageService messages, IRecoveryService recovery, ISafeService safes, ISafeContext safeContext, PartyType partyType, string draftKey) { _parties = parties; _items = items; _units = units; _messages = messages; _recovery = recovery; _safes = safes; _safeContext = safeContext; _partyType = partyType; _draftKey = draftKey; Parties = []; Items = []; Lines = []; PaymentType = PaymentType.Cash; _ = LoadAsync(); }
    public ObservableCollection<PartyDto> Parties { get; } public ObservableCollection<ItemDto> Items { get; } public ObservableCollection<InvoiceLineEditor> Lines { get; }
    [ObservableProperty] private int selectedPartyId; [ObservableProperty] private ItemDto? selectedItem; [ObservableProperty] private decimal quantity = 1; [ObservableProperty] private PaymentType paymentType; [ObservableProperty] private decimal paidAmount; [ObservableProperty] private string? notes;
    
    [ObservableProperty] private decimal currentSafeBalance;
    [ObservableProperty] private string balanceWarning = string.Empty;
    [ObservableProperty] private bool isInsufficientFunds;

    public decimal Total => Lines.Sum(x => x.Total);
    public event EventHandler<bool>? RequestClose;
    private async Task LoadAsync() 
    { 
        foreach (var p in await _parties.LookupAsync(new PartySearchRequest { Type = _partyType, IsActive = true })) Parties.Add(p); 
        foreach (var i in await _items.SearchAsync(null, null)) Items.Add(i); 
        SelectedPartyId = Parties.FirstOrDefault()?.Id ?? 0; 
        
        if (_partyType == PartyType.Supplier)
        {
            var activeSafeId = _safeContext.CurrentSafeId ?? (await _safes.GetDefaultCashSafeAsync()).Id;
            CurrentSafeBalance = await _safes.GetBalanceAsync(activeSafeId);
        }

        var draft = await _recovery.LoadDraftAsync<List<InvoiceLineEditor>>(_draftKey); 
        if (draft != null) { foreach(var d in draft) Lines.Add(d); OnPropertyChanged(nameof(Total)); } 
    }

    partial void OnPaidAmountChanged(decimal value) => UpdateBalanceInfo();

    private void UpdateBalanceInfo()
    {
        if (_partyType == PartyType.Supplier && PaidAmount > CurrentSafeBalance)
        {
            IsInsufficientFunds = true;
            decimal deficit = PaidAmount - CurrentSafeBalance;
            BalanceWarning = $"رصيد الخزنة غير كافٍ! العجز: {deficit:N2} ج.م";
        }
        else
        {
            IsInsufficientFunds = false;
            BalanceWarning = string.Empty;
        }
    }
    
    [RelayCommand] 
    private void AddLine() 
    { 
        if (SelectedItem is null) return; 
        
        var existing = Lines.FirstOrDefault(x => x.ItemId == SelectedItem.Id);
        if (existing != null)
        {
            existing.Quantity += Quantity;
        }
        else
        {
            Lines.Add(new InvoiceLineEditor { ItemId = SelectedItem.Id, ItemName = SelectedItem.Name, UnitId = SelectedItem.BaseUnitId, Quantity = Quantity, UnitPrice = SelectedItem.SalePrice > 0 ? SelectedItem.SalePrice : SelectedItem.PurchasePrice }); 
        }
        
        OnPropertyChanged(nameof(Total)); 
        _ = _recovery.SaveDraftAsync(_draftKey, Lines.ToList()); 
    }

    [RelayCommand] private void QuickBread() { SelectedItem = Items.FirstOrDefault(x => x.Code == "BREAD-001"); Quantity = 1; AddLine(); }
    [RelayCommand] private async Task SaveAndPostAsync() { var saved = await SaveCoreAsync(); if (!saved.Succeeded || saved.InvoiceId is null) { _messages.ShowError(saved.ErrorMessage ?? Loc.ErrSaveFailed); return; } var posted = await PostCoreAsync(saved.InvoiceId.Value); if (!posted.Succeeded) { _messages.ShowError(posted.ErrorMessage ?? Loc.ErrPostFailed); return; } await _recovery.DeleteDraftAsync(_draftKey); RequestClose?.Invoke(this, true); }
    [RelayCommand] private async Task Cancel() { await _recovery.DeleteDraftAsync(_draftKey); RequestClose?.Invoke(this, false); }
    protected abstract Task<(bool Succeeded, string? ErrorMessage, int? InvoiceId)> SaveCoreAsync();
    protected abstract Task<(bool Succeeded, string? ErrorMessage)> PostCoreAsync(int id);
}
