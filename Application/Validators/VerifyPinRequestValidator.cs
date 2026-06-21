using DotnetRoutix.Server.Application.DTOs;
using FluentValidation;

namespace DotnetRoutix.Server.Application.Validators;

public sealed class VerifyPinRequestValidator : AbstractValidator<VerifyPinRequest>
{
    public VerifyPinRequestValidator()
    {
        RuleFor(x => x.UserId)
            .GreaterThan(0);

        RuleFor(x => x.Pin)
            .NotEmpty()
            .Length(4)
            .Matches("^[0-9]{4}$");
    }
}
