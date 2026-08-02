using System;
using System.IO;
using System.Threading.Tasks;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Reporting.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Bakery.Reporting.Services;

public class ReportPdfGenerator : IPdfExportService
{
    private readonly IPermissionService _permissionService;

    public ReportPdfGenerator(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    static ReportPdfGenerator()
    {
        // Register QuestPDF Community License once at runtime
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task ExportToPdfAsync(object documentData, string destinationPath)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsExport);
        if (documentData is not PdfReportRequest request)
        {
            throw new ArgumentException("Invalid document data type. Expected PdfReportRequest.", nameof(documentData));
        }

        var document = new GenericReportDocument(
            request.Title,
            request.Data,
            request.StartDate,
            request.EndDate,
            request.SummaryCards
        );

        // Render PDF directly to byte array using QuestPDF fluent engine
        var bytes = document.GeneratePdf();

        // Save file output to the specified destination path
        await File.WriteAllBytesAsync(destinationPath, bytes);
    }
}
