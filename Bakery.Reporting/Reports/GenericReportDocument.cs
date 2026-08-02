using System;
using System.Collections.Generic;
using System.Linq;
using Bakery.Reporting.Reports.Base;
using Bakery.Reporting.Reports.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Bakery.Reporting.Reports;

public sealed class GenericReportDocument : BaseReport
{
    private readonly IEnumerable<object> _data;
    private readonly List<(string Title, string Value, string? Suffix)>? _summaryCards;

    public GenericReportDocument(
        string reportTitle,
        IEnumerable<object> data,
        DateTime? startDate = null,
        DateTime? endDate = null,
        List<(string Title, string Value, string? Suffix)>? summaryCards = null)
        : base(reportTitle, startDate, endDate)
    {
        _data = data;
        _summaryCards = summaryCards;
    }

    protected override void ComposeContent(IContainer container)
    {
        container.PaddingTop(10).Column(col =>
        {
            // 1. Compose Summary Cards if present
            if (_summaryCards != null && _summaryCards.Count > 0)
            {
                col.Item().Element(c => ReportComponents.SummaryGrid(c, _summaryCards));
            }

            // 2. Compose Table
            if (_data == null || !_data.Any())
            {
                col.Item().Padding(12).AlignRight().Text("لا توجد بيانات متاحة لعرضها.").Style(ReportTheme.SubtitleStyle);
                return;
            }

            col.Item().Element(c => ReportComponents.SectionHeader(c, "جدول البيانات والتفاصيل"));

            col.Item().PaddingTop(4).Table(table =>
            {
                var firstItem = _data.First();
                var properties = firstItem.GetType().GetProperties();

                // Define columns dynamically with generic proportional weights
                table.ColumnsDefinition(columns =>
                {
                    foreach (var prop in properties)
                    {
                        var weight = GetColumnWeight(prop.Name);
                        columns.RelativeColumn(weight);
                    }
                });

                // Define headers
                table.Header(header =>
                {
                    foreach (var prop in properties)
                    {
                        var columnName = prop.Name.Replace("_", " ");
                        header.Cell().Element(ReportComponents.TableHeaderCell).AlignRight().Text(columnName).Style(ReportTheme.TableHeaderStyle);
                    }
                });

                // Define rows
                int rowIndex = 0;
                foreach (var rowItem in _data)
                {
                    bool isEvenRow = rowIndex % 2 == 0;
                    foreach (var prop in properties)
                    {
                        var rawVal = prop.GetValue(rowItem);
                        var val = rawVal?.ToString() ?? "-";
                        table.Cell().Element(c => ReportComponents.TableCell(c, isEvenRow)).AlignRight().Text(val).Style(ReportTheme.TableCellStyle);
                    }
                    rowIndex++;
                }
            });
        });
    }

    private static float GetColumnWeight(string propName)
    {
        var name = propName.ToLowerInvariant().Replace("_", " ");
        if (name == "#" || name.Contains("تسلسل") || name.Contains("كود") || name.Contains("رمز") || name.Contains("id"))
            return 0.7f;
        if (name.Contains("بيان") || name.Contains("وصف") || name.Contains("اسم") || name.Contains("item") || name.Contains("description") || name.Contains("عنوان"))
            return 2.8f;
        if (name.Contains("تاريخ") || name.Contains("وقت") || name.Contains("date") || name.Contains("time"))
            return 1.4f;
        if (name.Contains("حالة") || name.Contains("نوع") || name.Contains("طريقة") || name.Contains("وحدة"))
            return 1.1f;
        return 1.3f;
    }
}
