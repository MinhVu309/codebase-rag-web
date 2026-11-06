using FluentValidation;

namespace Application.Projects;

public static class ProjectRules
{
    public const int MaxNameLength = 100;
}

public class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().Must(n => n is null || n.Trim().Length > 0)
            .WithMessage("'Name' must not be empty.")
            .Must(n => n is null || n.Trim().Length <= ProjectRules.MaxNameLength)
            .WithMessage($"'Name' must be at most {ProjectRules.MaxNameLength} characters.");
}

public class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().Must(n => n is null || n.Trim().Length > 0)
            .WithMessage("'Name' must not be empty.")
            .Must(n => n is null || n.Trim().Length <= ProjectRules.MaxNameLength)
            .WithMessage($"'Name' must be at most {ProjectRules.MaxNameLength} characters.");
}
