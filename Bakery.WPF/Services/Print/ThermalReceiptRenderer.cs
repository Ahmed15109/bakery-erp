using System.Globalization;
using System.Text;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;

namespace Bakery.WPF.Services.Print;

public sealed class ThermalReceiptRenderer : IReceiptRenderer
{
    private const string Separator = "------------------------------------------";

    public string Render(InvoicePrintDto invoice, ReceiptRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var builder = new StringBuilder();
        builder.AppendLine(string.IsNullOrWhiteSpace(invoice.BusinessName) ? Loc.AppTitle : invoice.BusinessName);
        if (!string.IsNullOrWhiteSpace(invoice.BranchName))
            builder.AppendLine(invoice.BranchName);
        builder.AppendLine(invoice.DocumentType);
        builder.AppendLine(Separator);
        builder.AppendLine($"رقم الفاتورة: {invoice.InvoiceNumber}");
        builder.AppendLine($"التاريخ: {invoice.Date:yyyy-MM-dd HH:mm}");
        builder.AppendLine($"الكاشير: {ValueOrDash(invoice.Cashier)}");
        builder.AppendLine($"العميل/المورد: {ValueOrDash(invoice.PartyName)}");
        builder.AppendLine($"طريقة الدفع: {PaymentName(invoice.PaymentType)}");
        builder.AppendLine(Separator);
        builder.AppendLine("الصنف");
        builder.AppendLine("الكمية × سعر الوحدة = الإجمالي");
        builder.AppendLine(Separator);

        foreach (var line in invoice.Lines)
        {
            builder.AppendLine(line.ItemName);
            var quantity = Number(line.Quantity);
            var unit = string.IsNullOrWhiteSpace(line.UnitName) ? string.Empty : $" {line.UnitName}";
            builder.AppendLine($"{quantity}{unit} × {Money(line.UnitPrice)} = {Money(line.Total)}");
        }

        var subtotal = invoice.Total + invoice.Discount - invoice.Tax;
        builder.AppendLine(Separator);
        builder.AppendLine($"الإجمالي قبل الخصم والضريبة: {Money(subtotal)}");
        builder.AppendLine($"الخصم: {Money(invoice.Discount)}");
        builder.AppendLine($"الضريبة: {Money(invoice.Tax)}");
        builder.AppendLine($"الإجمالي: {Money(invoice.Total)}");
        builder.AppendLine($"المدفوع: {Money(invoice.Paid)}");
        builder.AppendLine($"المتبقي: {Money(invoice.Remaining)}");
        builder.AppendLine(Separator);
        if (context.WorkingDayId.HasValue)
        {
            builder.AppendLine($"يوم العمل: {context.WorkingDayId.Value} | {context.BusinessDate:yyyy-MM-dd}");
        }
        builder.AppendLine($"طبع بواسطة: {ValueOrDash(context.PrintedBy)} | {context.PrintedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine(Separator);
        builder.AppendLine(string.IsNullOrWhiteSpace(invoice.Footer) ? Loc.ReceiptFooter : invoice.Footer);
        return builder.ToString();
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string PaymentName(PaymentType paymentType) => paymentType switch
    {
        PaymentType.Cash => "نقدي",
        PaymentType.Credit => "آجل",
        PaymentType.Mixed => "مختلط",
        _ => paymentType.ToString()
    };
}
