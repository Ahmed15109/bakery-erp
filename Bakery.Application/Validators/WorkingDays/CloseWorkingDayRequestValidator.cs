using Bakery.Application.DTOs;
using FluentValidation;

namespace Bakery.Application.Validators.WorkingDays;

public sealed class CloseWorkingDayRequestValidator : AbstractValidator<CloseWorkingDayRequest>
{
    public CloseWorkingDayRequestValidator()
    {
        RuleFor(request => request.CarryOverBalance).GreaterThanOrEqualTo(0);
        RuleFor(request => request.TransferredToMainSafe).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Notes).MaximumLength(500);
        RuleFor(request => request.OverrideReason)
            .NotEmpty()
            .When(request => request.AdminOverride)
            .WithMessage("تجاوز المدير يتطلب سبباً.");
        RuleFor(request => request.OverrideReason).MaximumLength(500);
    }
}
