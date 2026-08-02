using Bakery.Application.DTOs.Accounting;

namespace Bakery.Application.Interfaces;

public interface IReceiptRenderer
{
    string Render(InvoicePrintDto invoice, ReceiptRenderContext context);
}

public interface IReceiptPrintService
{
    Task PrintReceiptAsync(object documentData, string printerName = "", bool silent = false);
}

public interface IReportPrintService
{
    Task PrintReportAsync(object documentData, string printerName = "", bool silent = false);
}

public interface IPdfExportService
{
    Task ExportToPdfAsync(object documentData, string destinationPath);
}

public interface IExcelExportService
{
    Task ExportToExcelAsync(IEnumerable<object> data, string destinationPath);
}
