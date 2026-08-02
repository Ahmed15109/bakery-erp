using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class TreasuryViewModel : ViewModelBase, IDisposable
{
    private readonly ISafeService _safeService;
    private readonly IWorkingDayService _workingDayService;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IPermissionService _permissionService;
    private readonly IReportPrintService _printService;
    private readonly ISafeContext _safeContext;
    private readonly ISafeSwitchService _safeSwitchService;
    private readonly IOperationalContextRefreshNotifier? _refreshNotifier;
    private readonly EventHandler<SafeChangedEventArgs> _safeChangedHandler;

    private CancellationTokenSource? _loadCancellation;
    private readonly SemaphoreSlim _databaseLoadGate = new(1, 1);
    private long _loadVersion;
    private bool _suppressSelectionLoad;
    private bool _suppressFilterLoad;
    private bool _disposed;

    public TreasuryViewModel(
        ISafeService safeService,
        IWorkingDayService workingDayService,
        IMessageService messageService,
        IDialogService dialogService,
        IPermissionService permissionService,
        IReportPrintService printService,
        ISafeContext safeContext,
        ISafeSwitchService safeSwitchService,
        IOperationalContextRefreshNotifier? refreshNotifier = null)
    {
        _safeService = safeService;
        _workingDayService = workingDayService;
        _messageService = messageService;
        _dialogService = dialogService;
        _permissionService = permissionService;
        _printService = printService;
        _safeContext = safeContext;
        _safeSwitchService = safeSwitchService;
        _refreshNotifier = refreshNotifier;

        Title = "إدارة الخزينة";
        BusinessDate = DateTime.Today;
        Safes = [];
        Transactions = [];
        MovementTypeFilters =
        [
            new("كل الحركات", null),
            new("رصيد افتتاحي", SafeMovementType.OpeningBalance),
            new("تحصيل مبيعات", SafeMovementType.SaleCollection),
            new("سداد مشتريات", SafeMovementType.PurchasePayment),
            new("مصروف", SafeMovementType.ExpensePayment),
            new("أجر", SafeMovementType.WagePayment),
            new("تحويل وارد", SafeMovementType.TransferIn),
            new("تحويل صادر", SafeMovementType.TransferOut),
            new("تسوية", SafeMovementType.Adjustment)
        ];
        selectedMovementTypeFilter = MovementTypeFilters[0];

        _safeChangedHandler = (_, args) => HandleExternalSafeChange(args.NewSafe);
        _safeContext.SafeChanged += _safeChangedHandler;
        if (_refreshNotifier is not null) _refreshNotifier.RefreshRequested += OnOperationalRefreshRequested;

        Initialization = InitializeAsync();
    }

    public Task Initialization { get; }
    public ObservableCollection<SafeDto> Safes { get; }
    public ObservableCollection<SafeMovementDto> Transactions { get; }
    public IReadOnlyList<TreasuryMovementTypeFilterOption> MovementTypeFilters { get; }

    [ObservableProperty] private int? selectedTreasuryId;
    [ObservableProperty] private TreasurySnapshotDto? treasurySummary;
    [ObservableProperty] private string currentSafeName = "لا توجد خزينة محددة";
    [ObservableProperty] private string currentTreasurySubtitle = string.Empty;
    [ObservableProperty] private decimal currentSafeBalance;
    [ObservableProperty] private decimal todayIncome;
    [ObservableProperty] private decimal todayExpenses;
    [ObservableProperty] private decimal openingBalance;
    [ObservableProperty] private decimal expectedCash;
    [ObservableProperty] private decimal todaySales;
    [ObservableProperty] private decimal carriedBalance;
    [ObservableProperty] private DateTime businessDate;
    [ObservableProperty] private string dayStatusText = "جاري التحميل...";
    [ObservableProperty] private bool isInitializationRequired;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string loadError = string.Empty;

    [ObservableProperty] private DateTime? ledgerStartDate;
    [ObservableProperty] private DateTime? ledgerEndDate;
    [ObservableProperty] private TreasuryMovementTypeFilterOption selectedMovementTypeFilter;
    [ObservableProperty] private string searchText = string.Empty;

    public SafeDto? SelectedTreasury => SelectedTreasuryId.HasValue
        ? Safes.FirstOrDefault(safe => safe.Id == SelectedTreasuryId.Value)
        : null;
    public bool HasSelectedTreasury => SelectedTreasuryId.HasValue;
    public bool HasTransactions => Transactions.Count > 0;
    public bool HasOpenDay => TreasurySummary?.WorkingDayStatus == WorkingDayStatus.Open;
    public string ActiveDateRangeText
    {
        get
        {
            var culture = CultureInfo.CurrentCulture;
            if (LedgerStartDate.HasValue && LedgerEndDate.HasValue)
                return $"الفترة: {LedgerStartDate.Value.ToString("dd/MM/yyyy", culture)} — {LedgerEndDate.Value.ToString("dd/MM/yyyy", culture)}";
            if (LedgerStartDate.HasValue)
                return $"من {LedgerStartDate.Value.ToString("dd/MM/yyyy", culture)}";
            if (LedgerEndDate.HasValue)
                return $"حتى {LedgerEndDate.Value.ToString("dd/MM/yyyy", culture)}";
            return "كل الفترات";
        }
    }

    partial void OnSelectedTreasuryIdChanged(int? value)
    {
        OnPropertyChanged(nameof(SelectedTreasury));
        OnPropertyChanged(nameof(HasSelectedTreasury));
        ClearTreasuryData();
        NotifyCommandStates();

        if (!_suppressSelectionLoad && value.HasValue)
        {
            _ = QueueTreasuryLoadAsync(value.Value, switchContext: true, debounce: true);
        }
    }

    partial void OnLedgerStartDateChanged(DateTime? value) => QueueFilterRefresh();
    partial void OnLedgerEndDateChanged(DateTime? value) => QueueFilterRefresh();
    partial void OnSelectedMovementTypeFilterChanged(TreasuryMovementTypeFilterOption value) => QueueFilterRefresh();
    partial void OnSearchTextChanged(string value) => QueueFilterRefresh();
    partial void OnIsLoadingChanged(bool value) => NotifyCommandStates();

    private void QueueFilterRefresh()
    {
        OnPropertyChanged(nameof(ActiveDateRangeText));
        if (!_suppressFilterLoad && SelectedTreasuryId.HasValue)
        {
            _ = QueueTreasuryLoadAsync(SelectedTreasuryId.Value, switchContext: false, debounce: true);
        }
    }

    public async Task InitializeAsync()
    {
        if (_disposed) return;

        try
        {
            IsLoading = true;
            LoadError = string.Empty;
            IReadOnlyList<SafeDto> permittedSafes;
            await _databaseLoadGate.WaitAsync();
            try
            {
                permittedSafes = await _safeService.ListSafesAsync();
            }
            finally
            {
                _databaseLoadGate.Release();
            }
            if (_disposed) return;

            _suppressSelectionLoad = true;
            try
            {
                Safes.Clear();
                foreach (var safe in permittedSafes) Safes.Add(safe);

                var candidate = Safes.FirstOrDefault(safe => safe.Id == SelectedTreasuryId)
                    ?? Safes.FirstOrDefault(safe => safe.Id == _safeContext.CurrentSafeId)
                    ?? Safes.FirstOrDefault();
                SelectedTreasuryId = candidate?.Id;
            }
            finally
            {
                _suppressSelectionLoad = false;
            }

            OnPropertyChanged(nameof(SelectedTreasury));
            OnPropertyChanged(nameof(HasSelectedTreasury));

            if (SelectedTreasuryId.HasValue)
            {
                await QueueTreasuryLoadAsync(SelectedTreasuryId.Value, switchContext: true, debounce: false);
            }
            else
            {
                ClearTreasuryData();
                LoadError = "لا توجد خزينة متاحة لهذا المستخدم.";
            }
        }
        catch (Exception ex)
        {
            ClearTreasuryData();
            LoadError = "تعذر تحميل بيانات الخزينة المحددة";
            System.Diagnostics.Debug.WriteLine(ex);
            _messageService.ShowError(LoadError);
        }
        finally
        {
            if (!_disposed) IsLoading = false;
        }
    }

    [RelayCommand]
    public Task RefreshAsync()
        => SelectedTreasuryId.HasValue
            ? QueueTreasuryLoadAsync(SelectedTreasuryId.Value, switchContext: false, debounce: false)
            : InitializeAsync();

    private async Task QueueTreasuryLoadAsync(int treasuryId, bool switchContext, bool debounce)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        var token = cancellation.Token;

        IsLoading = true;
        LoadError = string.Empty;

        try
        {
            if (debounce) await Task.Delay(220, token);
            if (SelectedTreasuryId != treasuryId) return;

            var startDate = LedgerStartDate;
            var endDate = LedgerEndDate;
            var movementType = SelectedMovementTypeFilter?.Value;
            var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

            TreasurySnapshotDto summary;
            IReadOnlyList<SafeMovementDto> movements;
            await _databaseLoadGate.WaitAsync(token);
            try
            {
                if (SelectedTreasuryId != treasuryId) return;

                var selected = Safes.FirstOrDefault(safe => safe.Id == treasuryId)
                    ?? throw new KeyNotFoundException("الخزينة المحددة لم تعد متاحة.");

                if (switchContext && _safeContext.CurrentSafeId != treasuryId)
                {
                    await _safeSwitchService.SwitchSafeAsync(selected);
                }

               
                summary = await _safeService.GetTreasurySnapshotAsync(treasuryId, token);
                token.ThrowIfCancellationRequested();
                movements = await _safeService.GetLedgerAsync(
                    treasuryId,
                    startDate,
                    endDate,
                    movementType: movementType,
                    search: search,
                    cancellationToken: token);
            }
            finally
            {
                _databaseLoadGate.Release();
            }

            if (token.IsCancellationRequested || version != _loadVersion || SelectedTreasuryId != treasuryId) return;
            if (summary.TreasuryId != treasuryId || movements.Any(movement => movement.TreasuryId != treasuryId))
            {
                throw new InvalidOperationException("تم رفض بيانات لا تخص الخزينة المحددة.");
            }

            ApplyTreasuryData(summary, movements);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer treasury selection or filter request owns the UI now
        }
        catch (Exception ex)
        {
            if (version != _loadVersion || SelectedTreasuryId != treasuryId) return;

            ClearTreasuryData();
            LoadError = "تعذر تحميل بيانات الخزينة المحددة";
            System.Diagnostics.Debug.WriteLine(ex);
            _messageService.ShowError(LoadError);

            if (ex is UnauthorizedAccessException or KeyNotFoundException)
            {
                await RecoverValidTreasuryAsync(treasuryId);
            }
        }
        finally
        {
            if (version == _loadVersion && !_disposed) IsLoading = false;
        }
    }

    private void ApplyTreasuryData(
        TreasurySnapshotDto summary,
        IReadOnlyList<SafeMovementDto> movements)
    {
        TreasurySummary = summary;
        CurrentSafeName = summary.TreasuryName;
        CurrentTreasurySubtitle = $"{summary.BranchName} • {GetSafeTypeText(summary.TreasuryType)}";
        CurrentSafeBalance = summary.CurrentBalance;
        TodayIncome = summary.TodayReceipts;
        TodayExpenses = summary.TodayPayments;
        OpeningBalance = summary.OpeningBalance;
        ExpectedCash = summary.ExpectedCash;
        TodaySales = summary.TodaySales;
        CarriedBalance = summary.CarriedBalance;

        Transactions.Clear();
        foreach (var movement in movements) Transactions.Add(movement);
        OnPropertyChanged(nameof(HasTransactions));

        if (summary.WorkingDayStatus is null || summary.BusinessDate is null)
        {
            DayStatusText = "لا يوجد يوم عمل";
            IsInitializationRequired = true;
            BusinessDate = DateTime.Today;
        }
        else
        {
            DayStatusText = summary.WorkingDayStatus == WorkingDayStatus.Open
                ? "يوم عمل مفتوح"
                : "يوم عمل مغلق";
            IsInitializationRequired = summary.WorkingDayStatus != WorkingDayStatus.Open;
            BusinessDate = (summary.WorkingDayStatus == WorkingDayStatus.Open
                    ? summary.BusinessDate.Value
                    : summary.BusinessDate.Value.AddDays(1))
                .ToDateTime(TimeOnly.MinValue);
        }

        NotifyCommandStates();
    }

    private void ClearTreasuryData()
    {
        TreasurySummary = null;
        CurrentSafeName = SelectedTreasury?.DisplayName ?? "لا توجد خزينة محددة";
        CurrentTreasurySubtitle = SelectedTreasury?.Subtitle ?? string.Empty;
        CurrentSafeBalance = 0;
        TodayIncome = 0;
        TodayExpenses = 0;
        OpeningBalance = 0;
        ExpectedCash = 0;
        TodaySales = 0;
        CarriedBalance = 0;
        Transactions.Clear();
        OnPropertyChanged(nameof(HasTransactions));
        DayStatusText = "جاري التحميل...";
    }

    private async Task RecoverValidTreasuryAsync(int failedTreasuryId)
    {
        IReadOnlyList<SafeDto> permittedSafes;
        await _databaseLoadGate.WaitAsync();
        try
        {
            permittedSafes = await _safeService.ListSafesAsync();
        }
        finally
        {
            _databaseLoadGate.Release();
        }
        var fallback = permittedSafes.FirstOrDefault(safe => safe.Id != failedTreasuryId);

        _suppressSelectionLoad = true;
        try
        {
            Safes.Clear();
            foreach (var safe in permittedSafes) Safes.Add(safe);
            SelectedTreasuryId = fallback?.Id;
        }
        finally
        {
            _suppressSelectionLoad = false;
        }

        OnPropertyChanged(nameof(SelectedTreasury));
        OnPropertyChanged(nameof(HasSelectedTreasury));
        if (fallback is not null)
        {
            await QueueTreasuryLoadAsync(fallback.Id, switchContext: true, debounce: false);
        }
    }

    private void HandleExternalSafeChange(SafeDto? safe)
    {
        if (_disposed || safe is null || safe.Id == SelectedTreasuryId) return;

        if (Safes.Any(candidate => candidate.Id == safe.Id))
        {
            SelectedTreasuryId = safe.Id;
        }
        else
        {
            _ = InitializeAsync();
        }
    }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        _suppressFilterLoad = true;
        try
        {
            LedgerStartDate = null;
            LedgerEndDate = null;
            SelectedMovementTypeFilter = MovementTypeFilters[0];
            SearchText = string.Empty;
        }
        finally
        {
            _suppressFilterLoad = false;
        }

        OnPropertyChanged(nameof(ActiveDateRangeText));
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanInitializeSystem))]
    private async Task InitializeSystemAsync()
    {
        var result = await _workingDayService.OpenDayAsync(new OpenWorkingDayRequest(
            DateOnly.FromDateTime(BusinessDate), 0, "تهيئة النظام من شاشة الخزينة"));

        if (!result.Succeeded)
        {
            _messageService.ShowError(result.ErrorMessage ?? "فشل فتح يوم العمل");
            return;
        }
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanEndDay))]
    private async Task EndDayAsync()
    {
        if (!HasOpenDay) return;
        var result = await _dialogService.ShowDialogAsync<CloseDayDialogViewModel>(viewModel => viewModel.LoadAsync());
        if (result.Result == true) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanTransaction))]
    private async Task TransactionAsync(string? type)
    {
        if (SelectedTreasuryId is not int treasuryId) return;
        var isDeposit = string.Equals(type, "Deposit", StringComparison.OrdinalIgnoreCase);
        var result = await _dialogService.ShowDialogAsync<TreasuryTransactionDialogViewModel>(
            viewModel => viewModel.InitializeAsync(treasuryId, isDeposit));
        if (result.Result == true) await NotifyOperationalRefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanReverse))]
    private async Task ReverseAsync(SafeMovementDto transaction)
    {
        if (transaction is null || transaction.TreasuryId != SelectedTreasuryId) return;
        if (!HasOpenDay)
        {
            _messageService.ShowError("لا يمكن إلغاء معاملة نقدية عندما يكون يوم العمل مغلقاً");
            return;
        }

        var result = await _dialogService.ShowDialogAsync<ReverseTransactionDialogViewModel>(viewModel =>
        {
            viewModel.Initialize(
                transaction.Id,
                transaction.TransactionNumber ?? string.Empty,
                transaction.Amount,
                transaction.ReasonText ?? string.Empty,
                transaction.CreatedBy ?? string.Empty,
                transaction.Date);
            return Task.CompletedTask;
        });

        if (result.Result == true)
        {
            await NotifyOperationalRefreshAsync();
            _messageService.ShowInfo("تم إلغاء المعاملة النقدية بنجاح.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private async Task TransferAsync()
    {
        if (SelectedTreasuryId is not int treasuryId) return;
        var result = await _dialogService.ShowDialogAsync<TreasuryTransferDialogViewModel>(
            viewModel => viewModel.InitializeAsync(treasuryId));
        if (result.Result == true) await NotifyOperationalRefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanManageSafes))]
    private async Task ManageSafesAsync()
    {
        await _dialogService.ShowDialogAsync<SafeManagementDialogViewModel>(viewModel => viewModel.InitializeAsync());
        await NotifyOperationalRefreshAsync();
    }

    private async Task NotifyOperationalRefreshAsync()
    {
        if (_refreshNotifier is not null)
        {
            await _refreshNotifier.RequestRefreshAsync();
        }
        else
        {
            await RefreshAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPrintReport))]
    private async Task PrintReportAsync()
    {
        if (SelectedTreasuryId is not int treasuryId) return;
        var loadVersion = _loadVersion;

        try
        {
            IsLoading = true;
            TreasuryReportDto report;
            await _databaseLoadGate.WaitAsync();
            try
            {
                report = await _safeService.GetTreasuryReportAsync(
                    treasuryId,
                    LedgerStartDate,
                    LedgerEndDate,
                    SelectedMovementTypeFilter?.Value,
                    string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim());
            }
            finally
            {
                _databaseLoadGate.Release();
            }

            if (SelectedTreasuryId != treasuryId || report.TreasuryId != treasuryId ||
                report.Movements.Any(movement => movement.TreasuryId != treasuryId))
            {
                return;
            }

            await _printService.PrintReportAsync(BuildPrintableReport(report), silent: false);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Print treasury report"));
        }
        finally
        {
            if (SelectedTreasuryId == treasuryId && _loadVersion == loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private static PdfReportRequest BuildPrintableReport(TreasuryReportDto report)
    {
        var summary = report.Summary;
        var summaryCards = new List<(string Title, string Value, string? Suffix)>
        {
            ("الرصيد الافتتاحي", summary.OpeningBalance.ToString("N2"), "ج.م"),
            ("مقبوضات اليوم", summary.TodayReceipts.ToString("N2"), "ج.م"),
            ("مدفوعات اليوم", summary.TodayPayments.ToString("N2"), "ج.م"),
            ("الرصيد الحالي", summary.CurrentBalance.ToString("N2"), "ج.م"),
            ("النقدية المتوقعة", summary.ExpectedCash.ToString("N2"), "ج.م")
        };

        var movements = report.Movements.Select((m, index) => new
        {
            التسلسل = index + 1,
            التاريخ_والوقت = m.Date.ToString("yyyy-MM-dd HH:mm"),
            رقم_الحركة = m.DisplayTransactionNumber,
            البيان = m.Description,
            الوارد = m.Incoming.HasValue ? m.Incoming.Value.ToString("N2") : "0.00",
            المنصرف = m.Outgoing.HasValue ? m.Outgoing.Value.ToString("N2") : "0.00",
            الرصيد_التراكمي = m.RunningBalance.ToString("N2")
        }).ToList();

        return new PdfReportRequest(
            Title: $"تقرير حركة الخزينة: {summary.TreasuryName}",
            Data: movements,
            StartDate: report.StartDate,
            EndDate: report.EndDate,
            SummaryCards: summaryCards
        );
    }

    private bool CanInitializeSystem()
        => !IsLoading && !HasOpenDay && _permissionService.HasPermission(PermissionKeys.WorkingDayOpen);
    private bool CanEndDay()
        => !IsLoading && HasOpenDay && _permissionService.HasPermission(PermissionKeys.WorkingDayClose);
    private bool CanTransaction(string? type)
    {
        if (IsLoading || !HasOpenDay || TreasurySummary is null) return false;
        if (string.Equals(type, "Deposit", StringComparison.OrdinalIgnoreCase)) return TreasurySummary.CanDeposit;
        if (string.Equals(type, "Withdraw", StringComparison.OrdinalIgnoreCase)) return TreasurySummary.CanWithdraw;
        return TreasurySummary.CanDeposit || TreasurySummary.CanWithdraw;
    }
    private bool CanReverse(SafeMovementDto transaction)
        => !IsLoading && transaction is not null && transaction.TreasuryId == SelectedTreasuryId &&
           transaction.Origin == CashMovementOrigin.Manual && !transaction.IsReversed &&
           transaction.Origin != CashMovementOrigin.Reverse && HasOpenDay &&
           _permissionService.HasPermission(PermissionKeys.CashReverseManualTransaction);
    private bool CanTransfer()
        => !IsLoading && HasOpenDay && TreasurySummary?.CanTransfer == true;
    private bool CanManageSafes()
        => !IsLoading && _permissionService.HasPermission(PermissionKeys.TreasuryManageSafes);
    private bool CanPrintReport()
        => !IsLoading && HasSelectedTreasury && _permissionService.HasPermission(PermissionKeys.ReportsFinancial);

    private void NotifyCommandStates()
    {
        InitializeSystemCommand.NotifyCanExecuteChanged();
        EndDayCommand.NotifyCanExecuteChanged();
        TransactionCommand.NotifyCanExecuteChanged();
        TransferCommand.NotifyCanExecuteChanged();
        ManageSafesCommand.NotifyCanExecuteChanged();
        PrintReportCommand.NotifyCanExecuteChanged();
    }

    private static string GetSafeTypeText(SafeType type) => type switch
    {
        SafeType.Main => "خزينة رئيسية",
        SafeType.Private => "خزينة خاصة",
        SafeType.Daily => "خزينة يومية",
        _ => "خزينة عادية"
    };

    public void Dispose()
    {
        _disposed = true;
        _safeContext.SafeChanged -= _safeChangedHandler;
        if (_refreshNotifier is not null) _refreshNotifier.RefreshRequested -= OnOperationalRefreshRequested;
        var cancellation = Interlocked.Exchange(ref _loadCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async Task OnOperationalRefreshRequested()
    {
        if (_disposed) return;
        try
        {
            await RefreshAsync();
        }
        catch
        {
            // Navigation may dispose this page while a shared refresh is in flight.
        }
    }
}

public sealed record TreasuryMovementTypeFilterOption(string Label, SafeMovementType? Value);
