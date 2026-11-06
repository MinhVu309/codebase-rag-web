using FluentValidation;

namespace Application.Auth;

public static class AuthRules
{
    public const int MaxEmailLength = 254;
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
}

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(AuthRules.MaxEmailLength)
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(AuthRules.MinPasswordLength)
            .MaximumLength(AuthRules.MaxPasswordLength);
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(AuthRules.MaxEmailLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(AuthRules.MaxPasswordLength);
    }
}
