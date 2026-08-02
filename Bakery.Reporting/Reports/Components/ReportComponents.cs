using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Bakery.Reporting.Reports.Base;

namespace Bakery.Reporting.Reports.Components;

public static class ReportComponents
{
    public static void SectionHeader(IContainer container, string title)
    {
        container.PaddingBottom(8).Column(col =>
        {
            col.Item().AlignRight().Text(title).Style(ReportTheme.SectionHeaderStyle);
            col.Item().PaddingTop(2).Height(1.5f).Background(ReportTheme.AccentSecondary);
        });
    }

    public static void SummaryCard(IContainer container, string title, string value, string? suffix = "ج.م")
    {
        container
            .Border(1)
            .BorderColor(ReportTheme.BorderColor)
            .Background(ReportTheme.CardBackground)
            .PaddingVertical(8)
            .PaddingHorizontal(10)
            .Column(col =>
            {
                col.Item().AlignRight().Text(title).Style(ReportTheme.SummaryLabelStyle);
                col.Item().PaddingTop(2).AlignRight().Text(x =>
                {
                    x.Span(value).Style(ReportTheme.SummaryValueStyle);
                    if (!string.IsNullOrEmpty(suffix))
                    {
                        x.Span($" {suffix}").Style(ReportTheme.SummaryLabelStyle).FontSize(9.5f);
                    }
                });
            });
    }

    public static void SummaryGrid(IContainer container, System.Collections.Generic.List<(string Title, string Value, string? Suffix)> summaryCards)
    {
        if (summaryCards == null || summaryCards.Count == 0) return;

        container.PaddingBottom(15).Column(col =>
        {
            col.Item().Element(c => SectionHeader(c, "ملخص التقرير"));
            col.Item().PaddingTop(4).Row(row =>
            {
                row.Spacing(10);
                foreach (var card in summaryCards)
                {
                    row.RelativeItem().Element(c => SummaryCard(c, card.Title, card.Value, card.Suffix));
                }
            });
        });
    }

    public static IContainer TableHeaderCell(IContainer container)
    {
        return container
            .BorderBottom(1.5f)
            .BorderColor(ReportTheme.PrimaryColor)
            .Background(ReportTheme.TableHeaderBackground)
            .PaddingVertical(6)
            .PaddingHorizontal(8)
            .AlignCenter()
            .AlignMiddle();
    }

    public static IContainer TableCell(IContainer container, bool isEvenRow = false)
    {
        return container
            .BorderBottom(0.5f)
            .BorderColor(ReportTheme.BorderColor)
            .Background(isEvenRow ? ReportTheme.StripeColor : Colors.White)
            .PaddingVertical(6)
            .PaddingHorizontal(8)
            .AlignMiddle();
    }
}
