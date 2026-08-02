using Bakery.Domain.Entities;
using FluentValidation;

namespace Bakery.Application.Validators;

public sealed class SettingValidator : AbstractValidator<AppSetting>
{
    public SettingValidator()
    {
        RuleFor(setting => setting.Key).NotEmpty().MaximumLength(100);
        RuleFor(setting => setting.Value).MaximumLength(500);
    }
}
