using Bakery.Application.DTOs.Inventory;
using FluentValidation;

namespace Bakery.Application.Validators.Inventory;

public sealed class SaveItemRequestValidator : AbstractValidator<SaveItemRequest>
{
    public SaveItemRequestValidator()
    {
        RuleFor(request => request.Code).NotEmpty().MaximumLength(50);
        RuleFor(request => request.Name).NotEmpty().MaximumLength(200);
        RuleFor(request => request.PurchasePrice).GreaterThanOrEqualTo(0).WithMessage("سعر الشراء لا يمكن أن يكون سالباً.");
        RuleFor(request => request.SalePrice).GreaterThanOrEqualTo(0).WithMessage("سعر البيع لا يمكن أن يكون سالباً.");
        RuleFor(request => request.MinStockLevel).GreaterThanOrEqualTo(0);
        RuleFor(request => request.ReorderLevel).GreaterThanOrEqualTo(0);
    }
}

public sealed class SaveUnitRequestValidator : AbstractValidator<SaveUnitRequest>
{
    public SaveUnitRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(100);
        RuleFor(request => request.Symbol).NotEmpty().MaximumLength(20);
    }
}

public sealed class InventoryAdjustmentRequestValidator : AbstractValidator<InventoryAdjustmentRequest>
{
    public InventoryAdjustmentRequestValidator()
    {
        RuleFor(request => request.Quantity).GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من الصفر.");
        RuleFor(request => request.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CompleteStockCountRequestValidator : AbstractValidator<CompleteStockCountRequest>
{
    public CompleteStockCountRequestValidator()
    {
        RuleFor(request => request.SessionId).GreaterThan(0);
        RuleForEach(request => request.Lines).ChildRules(line =>
        {
            line.RuleFor(item => item.PhysicalQuantity).GreaterThanOrEqualTo(0);
        });
    }
}
