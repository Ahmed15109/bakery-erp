using Bakery.Domain.Entities;
using FluentValidation;

namespace Bakery.Application.Validators;

public sealed class ProductionOrderValidator : AbstractValidator<ProductionOrder>
{
    public ProductionOrderValidator()
    {
        RuleFor(p => p.ProducedItems).NotEmpty().WithMessage("Production cannot be empty.");
        RuleFor(p => p.ConsumedItems).NotEmpty().WithMessage("Production must have consumed items.");
        
        RuleForEach(p => p.ConsumedItems).ChildRules(items => 
        {
            items.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        });
        
        RuleForEach(p => p.ProducedItems).ChildRules(items => 
        {
            items.RuleFor(i => i.ActualProducedQty).GreaterThanOrEqualTo(0).WithMessage("Actual quantity cannot be negative.");
            items.RuleFor(i => i.ExpectedProducedQty).GreaterThan(0).WithMessage("Expected quantity must be greater than zero.");
        });
    }
}
