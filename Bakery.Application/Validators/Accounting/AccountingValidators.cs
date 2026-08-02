using Bakery.Application.DTOs.Accounting;
using FluentValidation;

namespace Bakery.Application.Validators.Accounting;

public sealed class SavePartyRequestValidator : AbstractValidator<SavePartyRequest>
{
    public SavePartyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.NationalId).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class SaveSaleInvoiceRequestValidator : AbstractValidator<SaveSaleInvoiceRequest>
{
    public SaveSaleInvoiceRequestValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("الفاتورة لا يمكن أن تكون فارغة.");
        RuleFor(x => x.PaidAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SafeId).NotNull().WithMessage("الخزنة مطلوبة للعملية المالية.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ItemId).GreaterThan(0);
            line.RuleFor(x => x.UnitId).GreaterThan(0);
            line.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من الصفر.");
            line.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class SavePurchaseInvoiceRequestValidator : AbstractValidator<SavePurchaseInvoiceRequest>
{
    public SavePurchaseInvoiceRequestValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("الفاتورة لا يمكن أن تكون فارغة.");
        RuleFor(x => x.PaidAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SafeId).NotNull().WithMessage("الخزنة مطلوبة للعملية المالية.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ItemId).GreaterThan(0);
            line.RuleFor(x => x.UnitId).GreaterThan(0);
            line.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من الصفر.");
            line.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}
