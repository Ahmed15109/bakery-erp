using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Bakery.Reporting.Reports.Base;

public static class ReportTheme
{
    public const string FontName = "Segoe UI";

    public static readonly string PrimaryColor = "#2D2421";          // Dark Charcoal Warm Brown
    public static readonly string AccentColor = "#4E342E";           // Deep Warm Brown Accent
    public static readonly string AccentSecondary = "#8D6E63";      // Muted Warm Cocoa
    public static readonly string CardBackground = "#FDFBF9";       // Soft Cream Neutral
    public static readonly string TableHeaderBackground = "#4E342E"; // Deep Brown Header Bar
    public static readonly string TableHeaderTextColor = "#FFFFFF";  // Crisp White Header Text
    public static readonly string BorderColor = "#E2E8F0";          // Light Warm Grey Divider
    public static readonly string StripeColor = "#F9F8F6";          // Soft Zebra Striping

    public static TextStyle SystemTitleStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(9.5f)
        .Bold()
        .FontColor(AccentSecondary);

    public static TextStyle TitleStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(20)
        .Bold()
        .FontColor(PrimaryColor);

    public static TextStyle SubtitleStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(9.5f)
        .FontColor("#6B7280");

    public static TextStyle HeaderMetaLabelStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(8.5f)
        .FontColor("#6B7280");

    public static TextStyle HeaderMetaValueStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(9)
        .Bold()
        .FontColor(PrimaryColor);

    public static TextStyle SectionHeaderStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(12)
        .Bold()
        .FontColor(PrimaryColor);

    public static TextStyle TableHeaderStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(9.5f)
        .Bold()
        .FontColor(TableHeaderTextColor);

    public static TextStyle TableCellStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(9)
        .FontColor("#1F2937");

    public static TextStyle SummaryLabelStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(8.5f)
        .FontColor("#4B5563");

    public static TextStyle SummaryValueStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(13)
        .Bold()
        .FontColor(PrimaryColor);

    public static TextStyle FooterStyle => TextStyle.Default
        .FontFamily(FontName)
        .FontSize(8.5f)
        .FontColor("#6B7280");
}
