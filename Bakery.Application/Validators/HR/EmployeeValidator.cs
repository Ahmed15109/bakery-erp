using Bakery.Domain.Entities;
using FluentValidation;

namespace Bakery.Application.Validators;

public sealed class EmployeeValidator : AbstractValidator<Employee>
{
    public EmployeeValidator()
    {
        RuleFor(e => e.Name).NotEmpty().WithMessage("اسم الموظف مطلوب.");
        RuleFor(e => e.Phone).MaximumLength(50).WithMessage("رقم الهاتف طويل جداً.");
        RuleFor(e => e.NationalId).MaximumLength(50).WithMessage("الرقم الوطني طويل جداً.");
        RuleFor(e => e.JobRoleId).GreaterThan(0).WithMessage("يجب اختيار وظيفة للموظف.");
    }
}
