using Bakery.Application.DTOs;
using FluentValidation;

namespace Bakery.Application.Validators;

public sealed class CreateBranchRequestValidator : AbstractValidator<CreateBranchRequest>
{
    public CreateBranchRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("كود الفرع مطلوب.")
            .MaximumLength(50).WithMessage("كود الفرع يجب ألا يتجاوز 50 حرفاً.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("اسم الفرع مطلوب.")
            .MaximumLength(150).WithMessage("اسم الفرع يجب ألا يتجاوز 150 حرفاً.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("الملاحظات يجب ألا تتجاوز 1000 حرف.");
    }
}

public sealed class UpdateBranchRequestValidator : AbstractValidator<UpdateBranchRequest>
{
    public UpdateBranchRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("رقم الفرع غير صحيح.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("كود الفرع مطلوب.")
            .MaximumLength(50).WithMessage("كود الفرع يجب ألا يتجاوز 50 حرفاً.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("اسم الفرع مطلوب.")
            .MaximumLength(150).WithMessage("اسم الفرع يجب ألا يتجاوز 150 حرفاً.");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("الملاحظات يجب ألا تتجاوز 1000 حرف.");
    }
}
