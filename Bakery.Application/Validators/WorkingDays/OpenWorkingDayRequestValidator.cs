using Bakery.Application.DTOs;
using FluentValidation;

namespace Bakery.Application.Validators.WorkingDays;

public sealed class OpenWorkingDayRequestValidator : AbstractValidator<OpenWorkingDayRequest>
{
    public OpenWorkingDayRequestValidator()
    {
        RuleFor(request => request.BusinessDate).NotEmpty();
        RuleFor(request => request.OpeningCash).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Notes).MaximumLength(500);
    }
}
