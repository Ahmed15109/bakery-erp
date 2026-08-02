using Bakery.Domain.Entities;
using FluentValidation;

namespace Bakery.Application.Validators;

public sealed class RecipeValidator : AbstractValidator<Recipe>
{
    public RecipeValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Recipe name is required.");
        RuleFor(r => r.ProducedItemId).GreaterThan(0).WithMessage("A produced item must be selected.");
        RuleFor(r => r.ProducedQuantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        RuleFor(r => r.ConsumedItems).NotEmpty().WithMessage("Recipe must contain consumed items.");
    }
}
