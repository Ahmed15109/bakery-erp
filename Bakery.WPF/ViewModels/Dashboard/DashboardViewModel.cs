using System.Collections.ObjectModel;
using Bakery.Application.Interfaces;
using Bakery.Application.DTOs;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using Bakery.WPF.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.Measure;
using Bakery.Application.Security;
using Bakery.Domain.Enums;
using SkiaSharp;
using System.Globalization;

namespace Bakery.WPF.ViewModels;

public sealed partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IUserSessionService _userSessionService;
    private readonly IWorkingDayService _workingDayService;
    private readonly INavigationService _navigationService;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IStockCalculationService _stockCalculationService;
    private readonly ISafeService _safeService;
    private readonly IPermissionService _permissionService;
    private readonly IBranchContext _branchContext;
    private readonly IBackupService _backupService;
    private readonly IBackupStatusNotifier _backupStatusNotifier;
    private readonly ISafeContext? _safeContext;
    private readonly ISafeSwitchService? _safeSwitchService;
    private readonly IOperationalContextRefreshNotifier? _refreshNotifier;
    private readonly EventHandler<SafeChangedEventArgs>? _safeChangedHandler;

    public DashboardViewModel(
        IUserSessionService userSessionService,
        IWorkingDayService workingDayService,
        INavigationService navigationService,
        IMessageService messageService,
        IDialogService dialogService,
        IStockCalculationService stockCalculationService,
        ISafeService safeService,
        IPermissionService permissionService,
        IBranchContext branchContext,
        IBackupService backupService,
        IBackupStatusNotifier backupStatusNotifier,
        ISafeContext? safeContext = null,
        ISafeSwitchService? safeSwitchService = null,
        IOperationalContextRefreshNotifier? refreshNotifier = null)
    {
        _userSessionService = userSessionService;
        _workingDayService = workingDayService;
        _navigationService = navigationService;
        _messageService = messageService;
        _dialogService = dialogService;
        _stockCalculationService = stockCalculationService;
        _safeService = safeService;
        _permissionService = permissionService;
        _branchContext = branchContext;
        _backupService = backupService;
        _backupStatusNotifier = backupStatusNotifier;
        _safeContext = safeContext;
        _safeSwitchService = safeSwitchService;
        _refreshNotifier = refreshNotifier;
        _backupStatusNotifier.StatusChanged += OnBackupStatusChanged;
        if (_refreshNotifier is not null) _refreshNotifier.RefreshRequested += OnOperationalRefreshRequested;
        if (_safeContext is not null)
        {
            _safeChangedHandler = OnSafeChanged;
            _safeContext.SafeChanged += _safeChangedHandler;
        }
        Title = Loc.Dashboard;
        Metrics = [];
        TreasuryAlerts = [];
        ConfigureCharts([]);
        InitializationTask = RefreshAsync();
    }

    public Task InitializationTask { get; }

    [ObservableProperty] private string currentUser = string.Empty;
    [ObservableProperty] private string currentDayStatus = Loc.NoOpenWorkingDay;
    [ObservableProperty] private decimal currentSafeBalance;
    [ObservableProperty] private decimal todaysSales;
    [ObservableProperty] private decimal todaysProduction;
    [ObservableProperty] private decimal productionEfficiency;
    [ObservableProperty] private decimal wasteCost;
    [ObservableProperty] private decimal employeeWages;
    [ObservableProperty] private int inventoryAdjustments;
    [ObservableProperty] private int lowStockAlerts;
    [ObservableProperty] private WorkingDaySummaryDto? daySummary;
    [ObservableProperty] private string activeBusinessDay = "—";
    [ObservableProperty] private string currentBranchName = "—";
    [ObservableProperty] private string treasuryBalanceText = "—";
    [ObservableProperty] private string currentSafeName = "—";
    [ObservableProperty] private ISeries[] salesSeries = [];
    [ObservableProperty] private ISeries[] productionSeries = [];
    [ObservableProperty] private Axis[] salesXAxes = [];
    [ObservableProperty] private Axis[] productionXAxes = [];
    [ObservableProperty] private Axis[] salesYAxes = [];
    [ObservableProperty] private Axis[] productionYAxes = [];
    [ObservableProperty] private int selectedChartTab; // 0 = Sales, 1 = Production
    [ObservableProperty] private string lastBackupText = "—";
    [ObservableProperty] private string backupCloudText = "—";
    [ObservableProperty] private string backupHealthText = "—";
    [ObservableProperty] private string backupHealthColor = "#6B625D";
    [ObservableProperty] private int pendingBackupUploads;

    public bool CanViewBackupStatus => _permissionService.HasPermission(PermissionKeys.BackupViewStatus);

    public bool IsSalesChartSelected => SelectedChartTab == 0;
    public bool IsProductionChartSelected => SelectedChartTab == 1;

    partial void OnSelectedChartTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsSalesChartSelected));
        OnPropertyChanged(nameof(IsProductionChartSelected));
    }

    [RelayCommand]
    private void SelectSalesTab() => SelectedChartTab = 0;

    [RelayCommand]
    private void SelectProductionTab() => SelectedChartTab = 1;

    public bool HasOpenDay => DaySummary?.Status == WorkingDayStatus.Open;
    public bool CanOpenDay => !HasOpenDay && _permissionService.HasPermission(PermissionKeys.WorkingDayOpen);

    partial void OnDaySummaryChanged(WorkingDaySummaryDto? value)
    {
        OnPropertyChanged(nameof(HasOpenDay));
        OnPropertyChanged(nameof(CanOpenDay));
        EndDayCommand.NotifyCanExecuteChanged();
        OpenDayCommand.NotifyCanExecuteChanged();
    }
    
    public ObservableCollection<TreasuryAlertViewModel> TreasuryAlerts { get; }

    public ObservableCollection<DashboardMetricViewModel> Metrics { get; }
    public ObservableCollection<DashboardMetricViewModel> PrimaryMetrics { get; } = [];
    public ObservableCollection<DashboardMetricViewModel> SecondaryMetrics { get; } = [];

    public bool ShowSalesChart => _permissionService.HasPermission(PermissionKeys.SalesView) || _permissionService.HasPermission(PermissionKeys.ReportsSales);
    public bool ShowProductionChart => _permissionService.HasPermission(PermissionKeys.ProductionView) ||
        _permissionService.HasPermission(PermissionKeys.ReportsProduction);

    [RelayCommand]
    public Task RefreshAsync()
    {
        CurrentUser = _userSessionService.CurrentUser?.DisplayName ?? Loc.NotSignedIn;
        return LoadSummaryAsync();
    }

    [RelayCommand(CanExecute = nameof(CanCreateSale))]
    private void NewSale()
    {
        _messageService.ShowInfo("مرحلة المبيعات قيد التطوير.");
    }

    private bool CanCreateSale() => _permissionService.HasPermission(PermissionKeys.SalesCreate);

    [RelayCommand(CanExecute = nameof(CanManageTreasury))]
    private void ManageTreasury()
    {
        _navigationService.NavigateTo<TreasuryViewModel>();
    }

    private bool CanManageTreasury() => _permissionService.HasPermission(PermissionKeys.TreasuryView);

    [RelayCommand]
    private void ManageBackups()
    {
        if (CanViewBackupStatus) _navigationService.NavigateTo<BackupManagementViewModel>();
    }

    [RelayCommand(CanExecute = nameof(CanEndDay))]
    private async Task EndDayAsync()
    {
        var result = await _dialogService.ShowDialogAsync<CloseDayDialogViewModel>(viewModel => viewModel.LoadAsync());
        if (result.Result == true) await RefreshAsync();
    }

    private bool CanEndDay() => HasOpenDay && _permissionService.HasPermission(PermissionKeys.WorkingDayClose);

    [RelayCommand(CanExecute = nameof(CanOpenDay))]
    private async Task OpenDayAsync()
    {
        var businessDate = DaySummary?.Status == WorkingDayStatus.Closed
            ? DaySummary.BusinessDate.AddDays(1)
            : DateOnly.FromDateTime(DateTime.Today);
        var result = await _workingDayService.OpenDayAsync(
            new OpenWorkingDayRequest(businessDate, 0, "فتح يوم العمل من لوحة التحكم"));
        if (!result.Succeeded)
        {
            _messageService.ShowError(result.ErrorMessage ?? "تعذر فتح يوم العمل.");
            return;
        }

        _messageService.ShowInfo("تم فتح يوم العمل بنجاح.");
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanCreateProduction))]
    private void NewProduction()
    {
        _navigationService.NavigateTo<ProductionViewModel>();
    }

    private bool CanCreateProduction() => _permissionService.HasPermission(PermissionKeys.ProductionCreate);

    private async Task LoadSummaryAsync()
    {
        WorkingDaySummaryDto? summary = null;
        if (_permissionService.HasAnyPermission(
                PermissionKeys.WorkingDayView,
                PermissionKeys.SalesView,
                PermissionKeys.ProductionView,
                PermissionKeys.TreasuryView,
                PermissionKeys.EmployeesViewSalary,
                PermissionKeys.InventoryView,
                PermissionKeys.ReportsSales,
                PermissionKeys.ReportsProduction,
                PermissionKeys.ReportsInventory,
                PermissionKeys.ReportsFinancial))
        {
            summary = await _workingDayService.GetCurrentDaySummaryAsync();
        }
        DaySummary = summary;
        CurrentBranchName = _branchContext.CurrentBranch?.Name ?? "الفرع غير محدد";
        LowStockAlerts = _permissionService.HasPermission(PermissionKeys.InventoryView) 
            || _permissionService.HasPermission(PermissionKeys.ReportsInventory)
            ? (await _stockCalculationService.GetLowStockItemsAsync()).Count 
            : 0;
        Metrics.Clear();
        PrimaryMetrics.Clear();
        SecondaryMetrics.Clear();

        var canViewTreasury = _permissionService.HasPermission(PermissionKeys.TreasuryView);
        var canViewSales = _permissionService.HasPermission(PermissionKeys.SalesView);
        var canViewProduction = _permissionService.HasPermission(PermissionKeys.ProductionView);
        var canViewInventory = _permissionService.HasPermission(PermissionKeys.InventoryView);
        var canViewEmployees = _permissionService.HasPermission(PermissionKeys.EmployeesViewSalary);

        if (summary is null)
        {
            CurrentDayStatus = "لا يوجد يوم عمل مفتوح";
            ActiveBusinessDay = "—";
            CurrentSafeBalance = 0;
            TodaysSales = 0;
            TodaysProduction = 0;
            ProductionEfficiency = 0;
            WasteCost = 0;
            EmployeeWages = 0;
            InventoryAdjustments = 0;
        }
        else
        {
            var status = summary.Status == WorkingDayStatus.Open
                ? summary.ReopenCount > 0 ? "مفتوح بعد إعادة الفتح" : "مفتوح"
                : "مغلق";
            CurrentDayStatus = $"يوم العمل: {status}";
            ActiveBusinessDay = summary.BusinessDate.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
            TodaysSales = summary.TotalSales;
            TodaysProduction = summary.ProductionCount;
            ProductionEfficiency = summary.ProductionEfficiency;
            WasteCost = summary.WasteCost;
            EmployeeWages = summary.Wages;
            InventoryAdjustments = summary.InventoryAdjustmentCount;
        }

        if (canViewTreasury)
        {
            var safesList = await _safeService.ListSafesAsync();
            var activeSafeId = _safeContext?.CurrentSafeId;
            var selectedSafe = activeSafeId.HasValue
                ? safesList.FirstOrDefault(safe => safe.Id == activeSafeId.Value)
                : null;

            if (selectedSafe is null && safesList.Count > 0)
            {
                selectedSafe = safesList.FirstOrDefault();
                if (selectedSafe is not null && _safeSwitchService is not null)
                {
                    try { await _safeSwitchService.SwitchSafeAsync(selectedSafe); } catch { }
                }
            }

            if (selectedSafe is not null)
            {
                CurrentSafeBalance = selectedSafe.Balance;
                TreasuryBalanceText = CurrentSafeBalance.ToString("N2", CultureInfo.CurrentCulture);
                CurrentSafeName = selectedSafe.DisplayName;
            }
            else
            {
                CurrentSafeBalance = 0;
                TreasuryBalanceText = "0.00";
                CurrentSafeName = "لا توجد خزنة محددة";
            }
        }
        else
        {
            CurrentSafeBalance = 0;
            TreasuryBalanceText = Loc.NoPermission;
            CurrentSafeName = "—";
        }

        // Primary KPIs (4 main business indicators)
        PrimaryMetrics.Add(canViewSales
            ? new(Loc.TodaysSales, TodaysSales.ToString("N2", CultureInfo.CurrentCulture), "CartOutline", "#2E7D32", "#E8F5E9")
            : new(Loc.TodaysSales, Loc.NoPermission, "CartOutline", "#9E9E9E", "#F3F3F3", false));

        PrimaryMetrics.Add(canViewProduction
            ? new(Loc.TodaysProduction, TodaysProduction.ToString("N0", CultureInfo.CurrentCulture), "Factory", "#C67C4E", "#FFF3E0")
            : new(Loc.TodaysProduction, Loc.NoPermission, "Factory", "#9E9E9E", "#F3F3F3", false));

        PrimaryMetrics.Add(canViewTreasury
            ? new("رصيد الخزنة الحالية", TreasuryBalanceText, "SafeSquareOutline", "#8B5E3C", "#F7EEE8", true, CurrentSafeName)
            : new("رصيد الخزنة الحالية", Loc.NoPermission, "SafeSquareOutline", "#9E9E9E", "#F3F3F3", false));

        PrimaryMetrics.Add(canViewTreasury
            ? new(Loc.ExpectedCashLabel, (summary?.ExpectedCash ?? 0).ToString("N2", CultureInfo.CurrentCulture), "CashCheck", "#2E7D32", "#E8F5E9")
            : new(Loc.ExpectedCashLabel, Loc.NoPermission, "CashCheck", "#9E9E9E", "#F3F3F3", false));

        SecondaryMetrics.Add(canViewEmployees
            ? new(Loc.EmployeeWagesCard, EmployeeWages.ToString("N2", CultureInfo.CurrentCulture), "AccountCashOutline", "#8B5E3C", "#F7EEE8")
            : new(Loc.EmployeeWagesCard, Loc.NoPermission, "AccountCashOutline", "#9E9E9E", "#F3F3F3", false));

        SecondaryMetrics.Add(canViewInventory
            ? new(Loc.WasteCostCard, WasteCost.ToString("N2", CultureInfo.CurrentCulture), "DeleteOutline", "#D32F2F", "#FDECEC")
            : new(Loc.WasteCostCard, Loc.NoPermission, "DeleteOutline", "#9E9E9E", "#F3F3F3", false));

        SecondaryMetrics.Add(canViewInventory
            ? new(Loc.InventoryAdjustments, InventoryAdjustments.ToString("N0", CultureInfo.CurrentCulture), "ClipboardEditOutline", "#F9A825", "#FFF8E1")
            : new(Loc.InventoryAdjustments, Loc.NoPermission, "ClipboardEditOutline", "#9E9E9E", "#F3F3F3", false));

        SecondaryMetrics.Add(canViewProduction
            ? new(Loc.Efficiency, ProductionEfficiency.ToString("N1", CultureInfo.CurrentCulture) + "%", "Speedometer", "#C67C4E", "#FFF3E0")
            : new(Loc.Efficiency, Loc.NoPermission, "Speedometer", "#9E9E9E", "#F3F3F3", false));

        foreach (var m in PrimaryMetrics) Metrics.Add(m);
        foreach (var m in SecondaryMetrics) Metrics.Add(m);

        await LoadTreasuryAlertsAsync();
        var trend = await _workingDayService.GetRecentDashboardTrendAsync();
        ConfigureCharts(trend);
        if (CanViewBackupStatus) await LoadBackupStatusAsync();
    }

    private async Task LoadBackupStatusAsync()
    {
        try
        {
            var status = await _backupService.GetStatusSummaryAsync();
            LastBackupText = status.LastSuccessfulLocalBackupUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
                ?? "لا توجد نسخة ناجحة";
            BackupCloudText = status.GoogleDriveConnected ? "Google Drive متصل" : "Google Drive غير متصل";
            PendingBackupUploads = status.PendingUploadCount;
            (BackupHealthText, BackupHealthColor) = status.Health switch
            {
                "Healthy" => ("سليم", "#2E7D32"),
                "Pending" => ("نسخة محلية سليمة والرفع معلق", "#C67C00"),
                "Failed" => ("تحتاج آخر محاولة إلى مراجعة", "#C62828"),
                "CloudAttention" => ("يحتاج Google Drive إلى إعادة ربط", "#C67C00"),
                _ => ("لم تُنشأ نسخة حديثة", "#C67C00")
            };
        }
        catch
        {
            BackupHealthText = "تعذر قراءة حالة النسخ الاحتياطي";
            BackupHealthColor = "#C62828";
        }
    }

    private void OnBackupStatusChanged(object? sender, EventArgs e)
        => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _ = LoadBackupStatusAsync());

    private static readonly SKTypeface ArabicTypeface = SKTypeface.FromFamilyName("Cairo") ?? SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;

    private void ConfigureCharts(IReadOnlyList<DashboardTrendPointDto> trend)
    {
        var labels = trend
            .Select(point => $"\u2066{point.BusinessDate:dd/MM}\u2069")
            .ToArray();
        var salesValues = trend.Select(point => (double)point.Sales).ToArray();
        var productionValues = trend.Select(point => (double)point.Production).ToArray();

        SalesSeries =
        [
            new LineSeries<double>
            {
                Name = Loc.Sales,
                Values = salesValues,
                Stroke = new SolidColorPaint(new SKColor(0x8B, 0x5E, 0x3C)) { StrokeThickness = 3, SKTypeface = ArabicTypeface },
                Fill = new SolidColorPaint(new SKColor(0xC6, 0x7C, 0x4E, 0x24)),
                GeometryStroke = new SolidColorPaint(new SKColor(0x8B, 0x5E, 0x3C)) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(SKColors.White),
                GeometrySize = 7,
                LineSmoothness = 0.35,
                AnimationsSpeed = TimeSpan.FromMilliseconds(450),
                EasingFunction = EasingFunctions.CubicOut,
                XToolTipLabelFormatter = point => $"{Loc.Date}: {LabelAt(labels, point.Index)}",
                YToolTipLabelFormatter = point => $"{Loc.Sales}: {FormatChartNumber(point.Coordinate.PrimaryValue)}"
            }
        ];

        ProductionSeries =
        [
            new ColumnSeries<double>
            {
                Name = Loc.Production,
                Values = productionValues,
                Fill = new SolidColorPaint(new SKColor(0xC6, 0x7C, 0x4E)) { SKTypeface = ArabicTypeface },
                Stroke = null,
                MaxBarWidth = 34,
                AnimationsSpeed = TimeSpan.FromMilliseconds(450),
                EasingFunction = EasingFunctions.CubicOut,
                XToolTipLabelFormatter = point => $"{Loc.Date}: {LabelAt(labels, point.Index)}",
                YToolTipLabelFormatter = point => $"{Loc.Production}: {FormatChartNumber(point.Coordinate.PrimaryValue)}"
            }
        ];

        SalesXAxes = [CreateRtlDateAxis(labels)];
        ProductionXAxes = [CreateRtlDateAxis(labels)];
        SalesYAxes = [CreateValueAxis(Loc.SalesValue)];
        ProductionYAxes = [CreateValueAxis(Loc.ProductionQuantity)];
    }

    private static Axis CreateRtlDateAxis(string[] labels)
        => new()
        {
            Labels = labels,
            IsInverted = true,
            MinStep = 1,
            ForceStepToMin = true,
            LabelsRotation = 0,
            TextSize = 11,
            LabelsPaint = new SolidColorPaint(new SKColor(0x5F, 0x57, 0x52)) { SKTypeface = ArabicTypeface },
            NamePaint = null,
            SeparatorsPaint = new SolidColorPaint(new SKColor(0xEC, 0xEC, 0xEC)) { StrokeThickness = 1 }
        };

    private static Axis CreateValueAxis(string name)
        => new()
        {
            Position = AxisPosition.End,
            MinLimit = 0,
            TextSize = 11,
            Labeler = value => FormatChartNumber(value),
            LabelsPaint = new SolidColorPaint(new SKColor(0x5F, 0x57, 0x52)) { SKTypeface = ArabicTypeface },
            NamePaint = null,
            SeparatorsPaint = new SolidColorPaint(new SKColor(0xEC, 0xEC, 0xEC)) { StrokeThickness = 1 }
        };

    private static string LabelAt(string[] labels, int index)
        => index >= 0 && index < labels.Length ? labels[index] : string.Empty;

    private static string FormatChartNumber(double value)
        => $"\u2066{value.ToString("N0", CultureInfo.GetCultureInfo("ar-EG"))}\u2069";

    private async Task LoadTreasuryAlertsAsync()
    {
        TreasuryAlerts.Clear();
        IReadOnlyList<Bakery.Application.DTOs.Accounting.SafeDto> safes;
        try
        {
            safes = await _safeService.ListSafesAsync();
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var safe in safes)
        {
            if (safe.Balance < 0)
            {
                TreasuryAlerts.Add(new TreasuryAlertViewModel
                {
                    SafeName = safe.DisplayName,
                    Balance = safe.Balance,
                    IsCritical = true,
                    Message = "رصيد سالب! يرجى مراجعة المعاملات."
                });
            }
            else if (safe.Balance < 500) // Arbitrary threshold for low balance
            {
                TreasuryAlerts.Add(new TreasuryAlertViewModel
                {
                    SafeName = safe.DisplayName,
                    Balance = safe.Balance,
                    IsCritical = false,
                    Message = "رصيد منخفض."
                });
            }
        }
    }

    private async Task OnOperationalRefreshRequested()
    {
        try
        {
            await RefreshAsync();
        }
        catch
        {
            // The normal page load path reports errors. A background refresh must
            // not crash the UI thread while navigation is changing pages.
        }
    }

    private void OnSafeChanged(object? sender, SafeChangedEventArgs e)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(async () => await RefreshAsync());
        }
        else
        {
            Task.Run(RefreshAsync);
        }
    }

    public void Dispose()
    {
        _backupStatusNotifier.StatusChanged -= OnBackupStatusChanged;
        if (_refreshNotifier is not null) _refreshNotifier.RefreshRequested -= OnOperationalRefreshRequested;
        if (_safeContext is not null && _safeChangedHandler is not null) _safeContext.SafeChanged -= _safeChangedHandler;
    }
}

public sealed partial class TreasuryAlertViewModel : ObservableObject
{
    [ObservableProperty] private string safeName = string.Empty;
    [ObservableProperty] private decimal balance;
    [ObservableProperty] private bool isCritical;
    [ObservableProperty] private string message = string.Empty;
}
