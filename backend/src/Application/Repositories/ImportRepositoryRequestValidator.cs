using FluentValidation;

namespace Application.Repositories;

public class ImportRepositoryRequestValidator : AbstractValidator<ImportRepositoryRequest>
{
    public ImportRepositoryRequestValidator()
    {
        RuleFor(x => x.RepoUrl).NotEmpty();
        RuleFor(x => x.RepoUrl)
            .Must(u => GitHubRepoUrl.TryNormalize(u, out _))
            .WithMessage("'RepoUrl' must be a public GitHub repository URL like https://github.com/<owner>/<repo>.")
            .When(x => !string.IsNullOrEmpty(x.RepoUrl));

        RuleFor(x => x.Ref)
            .Must(r => GitHubRepoUrl.IsValidRef(r))
            .WithMessage("'Ref' is not a valid branch, tag or commit name.")
            .When(x => x.Ref is not null);
    }
}
