using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Shared.Helpers;
using System.Windows;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;

namespace Bakery.WPF.Services.Print;

public class ThermalPrintService : IReceiptPrintService
{
    protected readonly IServiceProvider _serviceProvider;
    private readonly IReceiptRenderer _receiptRenderer;

    public ThermalPrintService(IServiceProvider serviceProvider, IReceiptRenderer receiptRenderer)
    {
        _serviceProvider = serviceProvider;
        _receiptRenderer = receiptRenderer;
    }

    public async Task PrintReceiptAsync(object documentData, string printerName = "", bool silent = false)
    {
        if (documentData is not InvoicePrintDto invoice)
            throw new ArgumentException("Thermal receipt printing requires invoice print data.", nameof(documentData));

        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IPermissionService>()
            .EnsureAnyPermission(PermissionKeys.SalesPrint, PermissionKeys.PurchasesPrint);
        var user = scope.ServiceProvider.GetRequiredService<IUserSessionService>().CurrentUser;
        var currentDay = await scope.ServiceProvider.GetRequiredService<IWorkingDayService>()
            .GetCurrentOpenDayAsync();
        var renderContext = new ReceiptRenderContext(
            currentDay?.Id,
            currentDay?.BusinessDate,
            user?.DisplayName ?? user?.UserName ?? "System",
            DateTime.Now);
        var receiptText = _receiptRenderer.Render(invoice, renderContext);

        var doc = new FlowDocument
        {
            PagePadding = new System.Windows.Thickness(0),
            ColumnWidth = 280,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11,
            FlowDirection = FlowDirection.RightToLeft,
            Language = XmlLanguage.GetLanguage("ar-EG")
        };

        var paragraph = new Paragraph(new Run(receiptText)) { Margin = new Thickness(0) };
        doc.Blocks.Add(paragraph);
        PrintDocument(doc, printerName, silent, $"{invoice.DocumentType} {invoice.InvoiceNumber}");
    }

    protected async Task AddAuditWatermarkAsync(Paragraph paragraph)
    {
        using var scope = _serviceProvider.CreateScope();
        var userSession = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        
        var currentDay = await workingDayService.GetCurrentOpenDayAsync();
        var user = userSession.CurrentUser?.DisplayName ?? "System";

        paragraph.Inlines.Add(new Run("\n--- بيانات التدقيق ---\n") { FontSize = 10, Foreground = System.Windows.Media.Brushes.Gray });
        if (currentDay != null)
        {
            paragraph.Inlines.Add(new Run($"رقم اليوم: {currentDay.Id} | تاريخ: {currentDay.BusinessDate}\n") { FontSize = 9 });
        }
        paragraph.Inlines.Add(new Run($"طبع بواسطة: {user} | {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n") { FontSize = 9 });
        paragraph.Inlines.Add(new Run($"{Loc.Separator}\n"));
    }

    protected void PrintDocument(FlowDocument doc, string printerName, bool silent, string title)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var pd = new PrintDialog();
            if (!string.IsNullOrWhiteSpace(printerName))
            {
                pd.PrintQueue = new System.Printing.LocalPrintServer().GetPrintQueue(printerName);
            }

            if (!silent && pd.ShowDialog() != true) return;

            IDocumentPaginatorSource idpSource = doc;
            pd.PrintDocument(idpSource.DocumentPaginator, title);
        });
    }
}

public class A4PrintService : ThermalPrintService, IReportPrintService
{
    public A4PrintService(IServiceProvider serviceProvider, IReceiptRenderer receiptRenderer)
        : base(serviceProvider, receiptRenderer) { }

    public async Task PrintReportAsync(object documentData, string printerName = "", bool silent = false)
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
            var pdfExportService = scope.ServiceProvider.GetService<IPdfExportService>()
                ?? new Bakery.Reporting.Services.ReportPdfGenerator(permissionService);
            var launcherService = scope.ServiceProvider.GetService<IFileLauncherService>();
            var appPaths = scope.ServiceProvider.GetService<IApplicationPathService>();

            PdfReportRequest request = documentData switch
            {
                PdfReportRequest pdfReq => pdfReq,
                string textData => ParseTextReportToPdfRequest(textData),
                _ => new PdfReportRequest("تقرير النظام", new[] { new { البيانات = documentData?.ToString() ?? "—" } })
            };

            var tempDir = appPaths?.TempReportsDirectory ?? Path.Combine(Path.GetTempPath(), "BakeryERP", "TempReports");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            var safeTitle = SanitizeFileName(request.Title);
            var tempPath = Path.Combine(tempDir, $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

            await pdfExportService.ExportToPdfAsync(request, tempPath);

            if (launcherService != null && File.Exists(tempPath))
            {
                launcherService.OpenFile(tempPath);
            }
        }
    }

    private static string SanitizeFileName(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Report";
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(title.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Report" : sanitized.Trim();
    }

    private static PdfReportRequest ParseTextReportToPdfRequest(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string title = "تقرير النظام";
        var summaryCards = new System.Collections.Generic.List<(string Title, string Value, string? Suffix)>();
        var tableRows = new System.Collections.Generic.List<object>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("---") || trimmed.StartsWith("===")) continue;

            if (trimmed.StartsWith("تقرير") && title == "تقرير النظام")
            {
                title = trimmed;
                continue;
            }

            if (trimmed.Contains(":") && !trimmed.Contains("|"))
            {
                var parts = trimmed.Split(':', 2);
                if (parts.Length == 2)
                {
                    var label = parts[0].Trim();
                    var val = parts[1].Trim();
                    if (!label.StartsWith("تاريخ") && !label.StartsWith("الفترة") && !label.StartsWith("الفرع"))
                    {
                        summaryCards.Add((label, val, "ج.م"));
                        continue;
                    }
                }
            }

            if (trimmed.Contains("|"))
            {
                var cells = trimmed.Split('|').Select(c => c.Trim()).ToArray();
                if (cells.Length >= 4)
                {
                    tableRows.Add(new
                    {
                        التاريخ_والوقت = cells[0],
                        رقم_الحركة = cells[1],
                        البيان = cells[cells.Length - 1],
                        تفاصيل_الحركة = string.Join(" | ", cells.Skip(2).Take(cells.Length - 3))
                    });
                }
            }
        }

        if (tableRows.Count == 0)
        {
            tableRows.Add(new { البيانات = text });
        }

        return new PdfReportRequest(title, tableRows, SummaryCards: summaryCards.Count > 0 ? summaryCards : null);
    }
}

public class PdfExportService : IPdfExportService
{
    private readonly IPermissionService _permissionService;

    public PdfExportService(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public async Task ExportToPdfAsync(object documentData, string destinationPath)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsExport);
        var generator = new Bakery.Reporting.Services.ReportPdfGenerator(_permissionService);
        await generator.ExportToPdfAsync(documentData, destinationPath);
    }
}

public class ExcelExportService : IExcelExportService
{
    private readonly IPermissionService _permissionService;

    public ExcelExportService(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public async Task ExportToExcelAsync(IEnumerable<object> data, string destinationPath)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsExport);
        if (data == null || !data.Any()) return;

        var lines = new List<string>();
        var props = data.First().GetType().GetProperties();

        lines.Add(string.Join(",", props.Select(p => $"\"{p.Name}\"")));

        foreach (var item in data)
        {
            lines.Add(string.Join(",", props.Select(p => 
            {
                var val = p.GetValue(item);
                return val is decimal d ? d.ToString("F2") : $"\"{val}\"";
            })));
        }

        await File.WriteAllLinesAsync(destinationPath, lines, System.Text.Encoding.UTF8);
    }
}
