using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Reporting.Services;
using FluentAssertions;
using QuestPDF.Fluent;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class PresentationDependencyRuntimeTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public PresentationDependencyRuntimeTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void WpfChart_RendersPixelsWithPinnedNativeDependencySet()
    {
        Exception? failure = null;
        var renderedWidth = 0;
        var renderedHeight = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var chart = new CartesianChart
                {
                    Width = 480,
                    Height = 260,
                    AnimationsSpeed = TimeSpan.Zero,
                    Series =
                    [
                        new LineSeries<double>
                        {
                            Values = [3d, 5d, 4d, 8d],
                            GeometrySize = 8
                        }
                    ]
                };
                chart.Measure(new Size(chart.Width, chart.Height));
                chart.Arrange(new Rect(0, 0, chart.Width, chart.Height));
                chart.UpdateLayout();
                var bitmap = new RenderTargetBitmap(
                    (int)chart.Width,
                    (int)chart.Height,
                    96,
                    96,
                    PixelFormats.Pbgra32);
                bitmap.Render(chart);
                renderedWidth = bitmap.PixelWidth;
                renderedHeight = bitmap.PixelHeight;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue();

        failure.Should().BeNull();
        renderedWidth.Should().Be(480);
        renderedHeight.Should().Be(260);
    }

    [Fact]
    public void SkiaNativeAssets_CreateDrawAndEncodeImage()
    {
        using var surface = SKSurface.Create(new SKImageInfo(64, 64));
        surface.Should().NotBeNull();
        surface.Canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.SaddleBrown, IsAntialias = true };
        surface.Canvas.DrawCircle(32, 32, 20, paint);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);

        encoded.Should().NotBeNull();
        encoded.Size.Should().BeGreaterThan(100);
    }

    [Fact]
    public async Task QuestPdfNativeRuntime_GeneratesReadablePdfArtifact()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BakeryERP", "PdfRuntimeTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "runtime-report.pdf");
        Directory.CreateDirectory(directory);
        try
        {
            using var scope = _fixture.ServiceProvider.CreateScope();
            var generator = new ReportPdfGenerator(
                scope.ServiceProvider.GetRequiredService<IPermissionService>());
            await generator.ExportToPdfAsync(
                new PdfReportRequest(
                    "تقرير اختبار التشغيل",
                    [new { Item = "خبز", Quantity = 12m, Amount = 60m }],
                    new DateTime(2026, 7, 1),
                    new DateTime(2026, 7, 22)),
                path);

            var bytes = await File.ReadAllBytesAsync(path);
            bytes.Length.Should().BeGreaterThan(1_000);
            System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task QuestPdfNativeRuntime_GeneratesProductionQualityPdfArtifactWithSummaryCardsAndTable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BakeryERP", "PdfRuntimeTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "treasury-report.pdf");
        Directory.CreateDirectory(directory);
        try
        {
            using var scope = _fixture.ServiceProvider.CreateScope();
            var generator = new ReportPdfGenerator(
                scope.ServiceProvider.GetRequiredService<IPermissionService>());

            var summaryCards = new System.Collections.Generic.List<(string Title, string Value, string? Suffix)>
            {
                ("الرصيد الافتتاحي", "10,000.00", "ج.م"),
                ("المقبوضات", "5,400.00", "ج.م"),
                ("المدفوعات", "1,200.00", "ج.م"),
                ("الرصيد الحالي", "14,200.00", "ج.م"),
                ("النقدية المتوقعة", "14,200.00", "ج.م")
            };

            var transactions = new[]
            {
                new { التسلسل = 1, التاريخ_والوقت = "2026-07-24 09:00", البيان = "رصيد افتتاح اليوم", الوارد = "10,000.00", المنصرف = "0.00", الرصيد_التراكمي = "10,000.00" },
                new { التسلسل = 2, التاريخ_والوقت = "2026-07-24 10:15", البيان = "تحصيل فاتورة مبيعات #1001", الوارد = "3,400.00", المنصرف = "0.00", الرصيد_التراكمي = "13,400.00" },
                new { التسلسل = 3, التاريخ_والوقت = "2026-07-24 11:30", البيان = "مشتريات دقيق وسكر", الوارد = "0.00", المنصرف = "1,200.00", الرصيد_التراكمي = "12,200.00" },
                new { التسلسل = 4, التاريخ_والوقت = "2026-07-24 12:45", البيان = "إيداع نقدي الخزينة", الوارد = "2,000.00", المنصرف = "0.00", الرصيد_التراكمي = "14,200.00" }
            };

            var request = new PdfReportRequest(
                Title: "تقرير حركة الخزينة اليومية",
                Data: transactions,
                StartDate: new DateTime(2026, 7, 24),
                EndDate: new DateTime(2026, 7, 24),
                SummaryCards: summaryCards
            );

            await generator.ExportToPdfAsync(request, path);

            var bytes = await File.ReadAllBytesAsync(path);
            bytes.Length.Should().BeGreaterThan(2_000);
            System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A4PrintService_PrintReportAsync_HandlesSpecialCharactersInReportTitleWithoutExceptions()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var printService = scope.ServiceProvider.GetService<Bakery.Application.Interfaces.IReportPrintService>()
            ?? new Bakery.WPF.Services.Print.A4PrintService(scope.ServiceProvider, null!);

        var summaryCards = new System.Collections.Generic.List<(string Title, string Value, string? Suffix)>
        {
            ("الرصيد الحالي", "500.00", "ج.م")
        };
        var rows = new[]
        {
            new { التسلسل = 1, التاريخ_والوقت = "2026-07-24 11:46", رقم_الحركة = "14", البيان = "تحويل إلى الخزينة الرئيسية", الوارد = "0.00", المنصرف = "400.00", الرصيد_التراكمي = "100.00" }
        };

        var requestWithSpecialChars = new PdfReportRequest(
            Title: "تقرير الخزينة: خزنة رصيد اليوم (رقم 3)",
            Data: rows,
            StartDate: new DateTime(2026, 7, 24),
            EndDate: new DateTime(2026, 7, 24),
            SummaryCards: summaryCards
        );

        // This call must complete cleanly without throwing NotSupportedException / ArgumentException due to invalid file path characters
        var act = async () => await printService.PrintReportAsync(requestWithSpecialChars);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void GenerateAllSamplePdfArtifacts_OutputsPdfsAndPngsForVisualReview()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var outputDir = @"C:\Users\Ahmed\.gemini\antigravity-ide\brain\2fa773b6-09b3-4e34-9acb-f3cbc37ed281\sample_pdfs";
        Directory.CreateDirectory(outputDir);

        // 1. Treasury Report
        var treasurySummary = new System.Collections.Generic.List<(string Title, string Value, string? Suffix)>
        {
            ("الرصيد الافتتاحي", "15,000.00", "ج.م"),
            ("إجمالي المقبوضات", "8,450.00", "ج.م"),
            ("إجمالي المدفوعات", "2,300.00", "ج.م"),
            ("الرصيد الحالي", "21,150.00", "ج.م"),
            ("النقدية المتوقعة", "21,150.00", "ج.م")
        };
        var treasuryData = new[]
        {
            new { التسلسل = 1, التاريخ_والوقت = "2026-07-24 08:30", البيان = "رصيد بداية اليوم بالخزينة الرئيسية", الوارد = "15,000.00", المنصرف = "0.00", الرصيد_التراكمي = "15,000.00" },
            new { التسلسل = 2, التاريخ_والوقت = "2026-07-24 09:45", البيان = "تحصيل فاتورة مبيعات جملة #1042 - عميل مخبز البركة", الوارد = "4,250.00", المنصرف = "0.00", الرصيد_التراكمي = "19,250.00" },
            new { التسلسل = 3, التاريخ_والوقت = "2026-07-24 10:15", البيان = "سداد فاتورة مشتريات خامات - شركة المطاحن المتحدة", الوارد = "0.00", المنصرف = "2,300.00", الرصيد_التراكمي = "16,950.00" },
            new { التسلسل = 4, التاريخ_والوقت = "2026-07-24 11:20", البيان = "مبيعات صالة وردية الصباح - نقدي", الوارد = "4,200.00", المنصرف = "0.00", الرصيد_التراكمي = "21,150.00" }
        };
        var treasuryDoc = new Bakery.Reporting.Reports.GenericReportDocument(
            "تقرير حركة الخزينة والخزائن الفرعية", treasuryData, new DateTime(2026, 7, 24), new DateTime(2026, 7, 24), treasurySummary);
        SaveDocArtifacts(treasuryDoc, outputDir, "Treasury_Report");

        // 2. Sales Report
        var salesSummary = new System.Collections.Generic.List<(string Title, string Value, string? Suffix)>
        {
            ("إجمالي المبيعات", "48,600.00", "ج.م"),
            ("إجمالي المدفوع", "42,100.00", "ج.م"),
            ("إجمالي المتبقي الآجل", "6,500.00", "ج.م")
        };
        var salesData = new[]
        {
            new { رقم_الفاتورة = "INV-2026-001", التاريخ_والوقت = "2026-07-24 09:10", العميل = "شركة الهدى لتوزيع الحلويات", طريقة_الدفع = "نقدي", الإجمالي = "12,400.00", المدفوع = "12,400.00", المتبقي = "0.00" },
            new { رقم_الفاتورة = "INV-2026-002", التاريخ_والوقت = "2026-07-24 10:30", العميل = "سوبر ماركت الأمانة", طريقة_الدفع = "آجل", الإجمالي = "18,200.00", المدفوع = "11,700.00", المتبقي = "6,500.00" },
            new { رقم_الفاتورة = "INV-2026-003", التاريخ_والوقت = "2026-07-24 11:50", العميل = "مخبز ومطعم الشرق", طريقة_الدفع = "نقدي", الإجمالي = "18,000.00", المدفوع = "18,000.00", المتبقي = "0.00" }
        };
        var salesDoc = new Bakery.Reporting.Reports.GenericReportDocument(
            "تقرير مبيعات الفواتير التفصيلي", salesData, new DateTime(2026, 7, 1), new DateTime(2026, 7, 24), salesSummary);
        SaveDocArtifacts(salesDoc, outputDir, "Sales_Report");

        // 3. Purchases Report
        var purchasesSummary = new System.Collections.Generic.List<(string Title, string Value, string? Suffix)>
        {
            ("إجمالي المشتريات", "35,000.00", "ج.م"),
            ("المسدد للموردين", "25,000.00", "ج.م"),
            ("المتبقي للموردين", "10,000.00", "ج.م")
        };
        var purchasesData = new[]
        {
            new { رقم_الفاتورة = "PO-2026-101", التاريخ_والوقت = "2026-07-20 14:00", المورد = "شركة مطاحن القاهرة الكبرى", حالة_الدفع = "جزئي", الإجمالي = "25,000.00", المدفوع = "15,000.00", المتبقي = "10,000.00" },
            new { رقم_الفاتورة = "PO-2026-102", التاريخ_والوقت = "2026-07-22 11:15", المورد = "مورد مستلزمات التعبئة والتغليف", حالة_الدفع = "نقدي بالكامل", الإجمالي = "10,000.00", المدفوع = "10,000.00", المتبقي = "0.00" }
        };
        var purchasesDoc = new Bakery.Reporting.Reports.GenericReportDocument(
            "تقرير حركة مشتريات الخامات والمستلزمات", purchasesData, new DateTime(2026, 7, 1), new DateTime(2026, 7, 24), purchasesSummary);
        SaveDocArtifacts(purchasesDoc, outputDir, "Purchases_Report");

        // 4. Inventory Report
        var inventorySummary = new System.Collections.Generic.List<(string Title, string Value, string? Suffix)>
        {
            ("إجمالي قيمة المخزون", "185,400.00", "ج.م"),
            ("أصناف تحت حد الطلب", "2", "صنف")
        };
        var inventoryData = new[]
        {
            new { كود_الصنف = "FLR-001", اسم_الصنف_والبيان = "دقيق فاخر استخراج 72% ممتاز ممتاز", الوحدة = "شكارة 50كجم", الكمية_المتاحة = "450.00", متوسط_التكلفة = "320.00", إجمالي_القيمة = "144,000.00", حد_الطلب = "100.00", حالة_الرصيد = "آمن" },
            new { كود_الصنف = "SGR-002", اسم_الصنف_والبيان = "سكر ناعم مكرر درجة اولى", الوحدة = "كجم", الكمية_المتاحة = "850.00", متوسط_التكلفة = "35.00", إجمالي_القيمة = "29,750.00", حد_الطلب = "200.00", حالة_الرصيد = "آمن" },
            new { كود_الصنف = "YST-003", اسم_الصنف_والبيان = "خميرة جافة طازجة طازجة", الوحدة = "كرتونة 10كجم", الكمية_المتاحة = "12.00", متوسط_التكلفة = "450.00", إجمالي_القيمة = "5,400.00", حد_الطلب = "20.00", حالة_الرصيد = "⚠️ تحت حد الطلب" },
            new { كود_الصنف = "OIL-004", اسم_الصنف_والبيان = "زيت نباتي نقاف ممتاز للخبز", الوحدة = "جالون 18لتر", الكمية_المتاحة = "7.00", متوسط_التكلفة = "900.00", إجمالي_القيمة = "6,300.00", حد_الطلب = "15.00", حالة_الرصيد = "⚠️ تحت حد الطلب" }
        };
        var inventoryDoc = new Bakery.Reporting.Reports.GenericReportDocument(
            "تقرير أرصده المخزون وتقييم الأصناف", inventoryData, null, null, inventorySummary);
        SaveDocArtifacts(inventoryDoc, outputDir, "Inventory_Report");
    }

    private static void SaveDocArtifacts(QuestPDF.Infrastructure.IDocument doc, string outputDir, string prefix)
    {
        var pdfPath = Path.Combine(outputDir, $"{prefix}.pdf");
        var pdfBytes = doc.GeneratePdf();
        File.WriteAllBytes(pdfPath, pdfBytes);

        try
        {
            var images = doc.GenerateImages();
            int pageIndex = 1;
            foreach (var imgBytes in images)
            {
                var imgPath = Path.Combine(outputDir, $"{prefix}_page{pageIndex}.png");
                File.WriteAllBytes(imgPath, imgBytes);
                pageIndex++;
            }
        }
        catch { }
    }
}
