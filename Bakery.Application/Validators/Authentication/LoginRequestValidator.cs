using Bakery.Application.DTOs;
using FluentValidation;

namespace Bakery.Application.Validators.Authentication;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.UserName).NotEmpty().MaximumLength(100);
        RuleFor(request => request.Password).NotEmpty().MinimumLength(4).MaximumLength(200);
    }
}
