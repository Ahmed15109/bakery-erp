using Bakery.Domain.Entities;
using FluentValidation;

namespace Bakery.Application.Validators;

public sealed class WasteEntryValidator : AbstractValidator<WasteEntry>
{
    public WasteEntryValidator()
    {
        RuleFor(w => w.ItemId).GreaterThan(0).WithMessage("An item must be selected.");
        RuleFor(w => w.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        RuleFor(w => w.Reason).NotEmpty().WithMessage("A reason for waste is required.");
    }
}
