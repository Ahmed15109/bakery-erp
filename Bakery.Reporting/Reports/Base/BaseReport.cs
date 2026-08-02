using System;
using Bakery.Reporting.Reports.Base;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Bakery.Reporting.Reports.Base;

public abstract class BaseReport : IDocument
{
    protected string ReportTitle { get; }
    protected DateTime? StartDate { get; }
    protected DateTime? EndDate { get; }

    protected BaseReport(string reportTitle, DateTime? startDate = null, DateTime? endDate = null)
    {
        ReportTitle = reportTitle;
        StartDate = startDate;
        EndDate = endDate;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container
            .Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.0f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily(ReportTheme.FontName));
                page.ContentFromRightToLeft();

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
    }

    protected virtual void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(12).Column(col =>
        {
            // Top Accent Line
            col.Item().Height(3).Background(ReportTheme.TableHeaderBackground);
            col.Item().PaddingTop(8).Row(row =>
            {
                // Right side (RTL Primary Title & Subtitle)
                row.RelativeItem().Column(titleCol =>
                {
                    titleCol.Item().Text("نظام إدارة المخبز ERP").Style(ReportTheme.SystemTitleStyle);
                    titleCol.Item().PaddingTop(2).Text(ReportTitle).Style(ReportTheme.TitleStyle);
                    if (StartDate.HasValue && EndDate.HasValue)
                    {
                        titleCol.Item().PaddingTop(2)
                            .Text($"الفترة من: {StartDate.Value:yyyy-MM-dd}  إلى: {EndDate.Value:yyyy-MM-dd}")
                            .Style(ReportTheme.SubtitleStyle);
                    }
                    else
                    {
                        titleCol.Item().PaddingTop(2).Text("تقرير مباشر - الحالة الحالية").Style(ReportTheme.SubtitleStyle);
                    }
                });

                // Left side (RTL Metadata Block)
                row.ConstantItem(200).AlignLeft().Column(metaCol =>
                {
                    metaCol.Item().AlignLeft().Text(x =>
                    {
                        x.Span("تاريخ الطباعة: ").Style(ReportTheme.HeaderMetaLabelStyle);
                        x.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Style(ReportTheme.HeaderMetaValueStyle);
                    });
                    metaCol.Item().PaddingTop(3).AlignLeft().Text(x =>
                    {
                        x.Span("حالة التقرير: ").Style(ReportTheme.HeaderMetaLabelStyle);
                        x.Span("تقرير موثق").Style(ReportTheme.HeaderMetaValueStyle);
                    });
                });
            });

            // Subtle Separator
            col.Item().PaddingTop(10).Height(1).Background(ReportTheme.BorderColor);
        });
    }

    protected abstract void ComposeContent(IContainer container);

    protected virtual void ComposeFooter(IContainer container)
    {
        container.BorderTop(1).BorderColor(ReportTheme.BorderColor).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().AlignRight().Text("نظام إدارة المخبز ERP | التقارير القياسية").Style(ReportTheme.FooterStyle);
            row.RelativeItem().AlignCenter().Text($"تاريخ الطباعة: {DateTime.Now:yyyy-MM-dd HH:mm}").Style(ReportTheme.FooterStyle);
            row.RelativeItem().AlignLeft().Text(x =>
            {
                x.Span("صفحة ").Style(ReportTheme.FooterStyle);
                x.CurrentPageNumber().Style(ReportTheme.FooterStyle);
                x.Span(" من ").Style(ReportTheme.FooterStyle);
                x.TotalPages().Style(ReportTheme.FooterStyle);
            });
        });
    }
}
