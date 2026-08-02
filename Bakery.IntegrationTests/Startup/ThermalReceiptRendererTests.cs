using Bakery.Application.DTOs.Accounting;
using Bakery.Domain.Enums;
using Bakery.WPF.Services.Print;
using FluentAssertions;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class ThermalReceiptRendererTests
{
    [Fact]
    public void Render_ShouldIncludeCompleteInvoiceLayout_AndEveryLine()
    {
        var invoice = new InvoicePrintDto(
            "S-20350722-0042",
            new DateTime(2035, 7, 22, 14, 30, 0),
            "Customer A",
            [
                new InvoicePrintLineDto("Croissant", 2m, 10m, 20m, "pcs"),
                new InvoicePrintLineDto("Chocolate Cake", 0.5m, 18m, 9m, "kg")
            ],
            29m,
            20m,
            9m,
            "Thermal",
            "Ahmed Bakery",
            "Main Branch",
            "Cashier One",
            "فاتورة بيع",
            PaymentType.Mixed,
            2m,
            1m,
            "Thank you");
        var context = new ReceiptRenderContext(
            17,
            new DateOnly(2035, 7, 22),
            "Supervisor",
            new DateTime(2035, 7, 22, 14, 31, 5));

        var rendered = new ThermalReceiptRenderer().Render(invoice, context);

        rendered.Should().Contain("Ahmed Bakery");
        rendered.Should().Contain("Main Branch");
        rendered.Should().Contain("فاتورة بيع");
        rendered.Should().Contain("S-20350722-0042");
        rendered.Should().Contain("2035-07-22 14:30");
        rendered.Should().Contain("Cashier One");
        rendered.Should().Contain("Customer A");
        rendered.Should().Contain("مختلط");
        rendered.Should().Contain("Croissant");
        rendered.Should().Contain("2 pcs × 10.00 = 20.00");
        rendered.Should().Contain("Chocolate Cake");
        rendered.Should().Contain("0.5 kg × 18.00 = 9.00");
        rendered.Should().Contain("الإجمالي قبل الخصم والضريبة: 30.00");
        rendered.Should().Contain("الخصم: 2.00");
        rendered.Should().Contain("الضريبة: 1.00");
        rendered.Should().Contain("الإجمالي: 29.00");
        rendered.Should().Contain("المدفوع: 20.00");
        rendered.Should().Contain("المتبقي: 9.00");
        rendered.Should().Contain("يوم العمل: 17 | 2035-07-22");
        rendered.Should().Contain("Supervisor");
        rendered.Should().Contain("Thank you");
        rendered.Should().NotContain("InvoicePrintDto {");
        rendered.Should().NotContain("System.Collections");
    }
}
