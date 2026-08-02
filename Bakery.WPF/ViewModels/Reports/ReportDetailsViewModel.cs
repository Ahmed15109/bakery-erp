using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.DTOs.Inventory;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Reporting.Interfaces;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class ReportDetailsViewModel : ViewModelBase
{
    private readonly ISaleInvoiceService _saleInvoiceService;
    private readonly IAccountingReportService _accountingService;
    private readonly IInventoryReportService _inventoryService;
    private readonly IProductionService _productionService;
    private readonly INavigationService _navigationService;
    private readonly IReportPrintService _printService;
    private readonly IMessageService _messageService;
    private readonly IExcelExportService _excelExportService;
    private readonly IPdfExportService _pdfExportService;
    private readonly IFileLauncherService _fileLauncherService;
    private readonly IPermissionService _permissionService;
    private readonly IApplicationPathService _applicationPaths;

    public ReportDetailsViewModel(
        ISaleInvoiceService saleInvoiceService,
        IAccountingReportService accountingService,
        IInventoryReportService inventoryService,
        IProductionService productionService,
        INavigationService navigationService,
        IReportPrintService printService,
        IMessageService messageService,
        IExcelExportService excelExportService,
        IPdfExportService pdfExportService,
        IFileLauncherService fileLauncherService,
        IPermissionService permissionService,
        IApplicationPathService applicationPaths)
    {
        _saleInvoiceService = saleInvoiceService;
        _accountingService = accountingService;
        _inventoryService = inventoryService;
        _productionService = productionService;
        _navigationService = navigationService;
        _printService = printService;
        _messageService = messageService;
        _excelExportService = excelExportService;
        _pdfExportService = pdfExportService;
        _fileLauncherService = fileLauncherService;
        _permissionService = permissionService;
        _applicationPaths = applicationPaths;

        GridRows = [];
        StartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        EndDate = DateTime.Today.AddDays(1).AddTicks(-1);
    }

    [ObservableProperty] private string reportName = string.Empty;
    [ObservableProperty] private ReportCategoryType categoryType;
    [ObservableProperty] private bool showDateFilter;
    [ObservableProperty] private DateTime? startDate;
    [ObservableProperty] private DateTime? endDate;
    [ObservableProperty] private string validationMessage = string.Empty;
    [ObservableProperty] private bool isCatalogVisible = true;

    public ObservableCollection<object> GridRows { get; }

    public void Initialize(ReportCategoryType type)
    {
        CategoryType = type;
        ReportName = type switch
        {
            ReportCategoryType.Sales => "المبيعات",
            ReportCategoryType.Production => "الإنتاج",
            ReportCategoryType.Inventory => "المخزون",
            ReportCategoryType.Accounts => "الحسابات",
            _ => "المبيعات"
        };
        Title = $"تقرير {ReportName}";
        ShowDateFilter = type != ReportCategoryType.Inventory;
        ValidateDates();
        _ = RefreshAsync();
    }

    partial void OnStartDateChanged(DateTime? value) => ValidateDates();
    partial void OnEndDateChanged(DateTime? value) => ValidateDates();

    private void ValidateDates()
    {
        if (ShowDateFilter && StartDate.HasValue && EndDate.HasValue && StartDate > EndDate)
        {
            ValidationMessage = "تاريخ البدء لا يمكن أن يكون بعد تاريخ الانتهاء.";
        }
        else
        {
            ValidationMessage = string.Empty;
        }

        RefreshCommand.NotifyCanExecuteChanged();
        PrintCommand.NotifyCanExecuteChanged();
        ExportExcelCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    private bool CanExecuteReportCommand() => string.IsNullOrEmpty(ValidationMessage) && HasCategoryPermission();

    [RelayCommand]
    private void Back()
    {
        _navigationService.NavigateTo<ReportsViewModel>();
    }

    [RelayCommand]
    private void ShowGenericReport()
    {
        IsCatalogVisible = false;
    }

    [RelayCommand]
    private void BackToCatalog()
    {
        IsCatalogVisible = true;
    }

    [RelayCommand]
    private void NavigateToCustomerStatement()
    {
        _navigationService.NavigateTo<PartyStatementViewModel>();
    }

    [RelayCommand]
    private void NavigateToSupplierStatement()
    {
        _navigationService.NavigateTo<PartyStatementViewModel>();
    }

    [RelayCommand]
    private void NavigateToEmployeeStatement()
    {
        _navigationService.NavigateTo<EmployeeLedgerViewModel>();
    }

    [RelayCommand]
    private void NavigateToInventoryMovements()
    {
        _navigationService.NavigateTo<InventoryMovementsViewModel>();
    }

    [RelayCommand]
    private void NavigateToStockCount()
    {
        _navigationService.NavigateTo<StockCountViewModel>();
    }

    [RelayCommand]
    private void NavigateToSalesWorkspace()
    {
        _navigationService.NavigateTo<InvoiceWorkspaceViewModel>();
    }

    [RelayCommand]
    private void NavigateToProductionWorkspace()
    {
        _navigationService.NavigateTo<ProductionViewModel>();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteReportCommand))]
    private async Task RefreshAsync()
    {
        try
        {
            _permissionService.EnsurePermission(GetCategoryPermission());
            GridRows.Clear();

            var start = StartDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var end = EndDate ?? DateTime.Today;

            if (CategoryType == ReportCategoryType.Sales)
            {
                var invoices = await _saleInvoiceService.ListAsync(InvoiceStatus.Posted);
                var sales = invoices
                    .Where(x => x.Date >= start && x.Date <= end)
                    .Select(x => new
                    {
                        رقم_الفاتورة = x.InvoiceNumber,
                        التاريخ = x.Date.ToString("yyyy-MM-dd HH:mm"),
                        العميل = x.PartyName,
                        طريقة_الدفع = x.PaymentType == PaymentType.Cash ? "نقدي" : "آجل",
                        الإجمالي = x.TotalAmount.ToString("N2"),
                        المدفوع = x.PaidAmount.ToString("N2"),
                        المتبقي = x.RemainingAmount.ToString("N2")
                    });

                foreach (var row in sales) GridRows.Add(row);
            }
            else if (CategoryType == ReportCategoryType.Inventory)
            {
                var stock = await _inventoryService.GetCurrentStockReportAsync();
                var rows = stock.Select(x => new
                {
                    كود_الصنف = x.Code,
                    اسم_الصنف = x.Name,
                    الوحدة = x.Unit,
                    الكمية_المتاحة = x.Quantity.ToString("N2"),
                    متوسط_التكلفة = x.UnitCost.ToString("N2"),
                    إجمالي_القيمة = x.Value.ToString("N2"),
                    حد_الطلب = x.MinStockLevel.ToString("N2"),
                    حالة_الطلب = x.IsBelowMinimum ? "⚠️ تحت الحد" : "آمن"
                });

                foreach (var row in rows) GridRows.Add(row);
            }
            else if (CategoryType == ReportCategoryType.Accounts)
            {
                var customers = await _accountingService.GetCustomerBalancesAsync();
                var suppliers = await _accountingService.GetSupplierBalancesAsync();

                var customerRows = customers.Select(x => new
                {
                    الاسم = x.Name,
                    النوع = "عميل",
                    الهاتف = x.Phone ?? "-",
                    العنوان = x.Address ?? "-",
                    الرصيد_الحالي = x.Balance.ToString("N2"),
                    الحالة = x.IsActive ? "نشط" : "موقوف"
                });

                var supplierRows = suppliers.Select(x => new
                {
                    الاسم = x.Name,
                    النوع = "مورد",
                    الهاتف = x.Phone ?? "-",
                    العنوان = x.Address ?? "-",
                    الرصيد_الحالي = x.Balance.ToString("N2"),
                    الحالة = x.IsActive ? "نشط" : "موقوف"
                });

                foreach (var row in customerRows.Concat(supplierRows)) GridRows.Add(row);
            }
            else if (CategoryType == ReportCategoryType.Production)
            {
                var orders = await _productionService.GetAllProductionOrdersAsync();
                var rows = orders
                    .Where(x => x.StartedAt >= start && x.StartedAt <= end)
                    .Select(x => new
                    {
                        رقم_الأمر = x.ProductionNumber,
                        الوصفة = x.Recipe?.Name ?? "-",
                        تاريخ_البدء = x.StartedAt.ToString("yyyy-MM-dd HH:mm"),
                        تاريخ_الانتهاء = x.CompletedAt?.ToString("yyyy-MM-dd HH:mm") ?? "-",
                        الحالة = x.Status switch
                        {
                            ProductionStatus.Completed => "مكتمل",
                            ProductionStatus.InProgress => "قيد التشغيل",
                            ProductionStatus.Cancelled => "ملغى",
                            _ => "مسودة"
                        }
                    });

                foreach (var row in rows) GridRows.Add(row);
            }
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Load report data"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanPrintReport))]
    private async Task PrintAsync()
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsPrint);
        if (GridRows.Count == 0)
        {
            _messageService.ShowError("لا توجد بيانات لطباعتها.");
            return;
        }

        try
        {
            var request = BuildPdfReportRequest();
            var tempDir = _applicationPaths.TempReportsDirectory;
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeTitle = new string(request.Title.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            var tempPath = Path.Combine(tempDir, $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            
            // Generate and save to temp path
            await _pdfExportService.ExportToPdfAsync(request, tempPath);

            // Open in default PDF browser/viewer using the file launcher service abstraction
            _fileLauncherService.OpenFile(tempPath);

            _messageService.ShowInfo("تم توليد التقرير وفتحه في المعاين الافتراضي.");
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Preview report"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private async Task ExportExcelAsync()
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsExport);
        if (GridRows.Count == 0)
        {
            _messageService.ShowError("لا توجد بيانات للتصدير.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"{ReportName}_{DateTime.Now:yyyyMMdd}"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _excelExportService.ExportToExcelAsync(GridRows, dialog.FileName);
                _messageService.ShowInfo("تم تصدير البيانات إلى Excel بنجاح.");
            }
            catch (Exception ex)
            {
                _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Export report to Excel"));
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private async Task ExportPdfAsync()
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsExport);
        if (GridRows.Count == 0)
        {
            _messageService.ShowError("لا توجد بيانات للتصدير.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = $"{ReportName}_{DateTime.Now:yyyyMMdd}"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var request = BuildPdfReportRequest();
                await _pdfExportService.ExportToPdfAsync(request, dialog.FileName);
                _messageService.ShowInfo("تم تصدير البيانات إلى PDF بنجاح.");
            }
            catch (Exception ex)
            {
                _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Export report to PDF"));
            }
        }
    }

    private PdfReportRequest BuildPdfReportRequest()
    {
        var summaryCards = new System.Collections.Generic.List<(string Title, string Value, string? Suffix)>();

        if (CategoryType == ReportCategoryType.Sales)
        {
            var totalAmount = GridRows.Sum(r => SafeParse(((dynamic)r).الإجمالي));
            var totalPaid = GridRows.Sum(r => SafeParse(((dynamic)r).المدفوع));
            var totalRemaining = GridRows.Sum(r => SafeParse(((dynamic)r).المتبقي));

            summaryCards.Add(("إجمالي المبيعات", totalAmount.ToString("N2"), "ج.م"));
            summaryCards.Add(("إجمالي المدفوع", totalPaid.ToString("N2"), "ج.م"));
            summaryCards.Add(("إجمالي المتبقي", totalRemaining.ToString("N2"), "ج.م"));
        }
        else if (CategoryType == ReportCategoryType.Inventory)
        {
            var totalValue = GridRows.Sum(r => SafeParse(((dynamic)r).إجمالي_القيمة));
            var lowStockCount = GridRows.Count(r => ((dynamic)r).حالة_الطلب == "⚠️ تحت الحد");

            summaryCards.Add(("إجمالي قيمة المخزون", totalValue.ToString("N2"), "ج.م"));
            summaryCards.Add(("أصناف تحت حد الطلب", lowStockCount.ToString(), "صنف"));
        }
        else if (CategoryType == ReportCategoryType.Accounts)
        {
            var customerCount = GridRows.Count(r => ((dynamic)r).النوع == "عميل");
            var supplierCount = GridRows.Count(r => ((dynamic)r).النوع == "مورد");

            summaryCards.Add(("عدد العملاء", customerCount.ToString(), "عميل"));
            summaryCards.Add(("عدد الموردين", supplierCount.ToString(), "مورد"));
        }
        else if (CategoryType == ReportCategoryType.Production)
        {
            var completedCount = GridRows.Count(r => ((dynamic)r).الحالة == "مكتمل");
            var inProgressCount = GridRows.Count(r => ((dynamic)r).الحالة == "قيد التشغيل");

            summaryCards.Add(("أوامر مكتملة", completedCount.ToString(), "أمر"));
            summaryCards.Add(("أوامر قيد التشغيل", inProgressCount.ToString(), "أمر"));
        }

        return new PdfReportRequest(
            Title: ReportName,
            Data: GridRows,
            StartDate: StartDate,
            EndDate: EndDate,
            SummaryCards: summaryCards
        );
    }

    private decimal SafeParse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var clean = value.Replace(",", "").Replace("ج.م", "").Trim();
        return decimal.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private bool HasCategoryPermission() => _permissionService.HasPermission(GetCategoryPermission());
    private bool CanPrintReport() => CanExecuteReportCommand() && _permissionService.HasPermission(PermissionKeys.ReportsPrint);
    private bool CanExportReport() => CanExecuteReportCommand() && _permissionService.HasPermission(PermissionKeys.ReportsExport);

    private string GetCategoryPermission() => CategoryType switch
    {
        ReportCategoryType.Sales => PermissionKeys.ReportsSales,
        ReportCategoryType.Production => PermissionKeys.ReportsProduction,
        ReportCategoryType.Inventory => PermissionKeys.ReportsInventory,
        ReportCategoryType.Accounts => PermissionKeys.ReportsFinancial,
        _ => PermissionKeys.ReportsSales
    };
}
