using System.Linq.Expressions;
using FluentValidation;

namespace Application.Chat;

public static class ChatRules
{
    public const int MaxTitleLength = 200;
    public const int MaxQuestionLength = 2000; // D22
    public const int DefaultTopK = 8;
    public const int MaxTopK = 20;             // D22
    public const string DefaultTitle = "New conversation";
    public const int MaxAutoTitleLength = 60;
    public const int DefaultHistoryLimit = 200;
    public const int MaxHistoryLimit = 500;
}

/// <summary>
/// One set of title rules for creating and renaming a conversation, so the two can never drift apart. Judged on the
/// trimmed value (which is also what gets stored): not blank, at most <see cref="ChatRules.MaxTitleLength"/>
/// characters, no control characters (line breaks, tabs, ...). A null title is not judged here: whether it is allowed
/// is up to the caller.
/// </summary>
internal static class ConversationTitleRules
{
    public static void AddTitleRules<T>(this AbstractValidator<T> validator, Expression<Func<T, string?>> title)
    {
        var get = title.Compile();
        validator.When(x => get(x) is not null, () =>
        {
            validator.RuleFor(title!)
                .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("'Title' must not be empty.");
            validator.When(x => !string.IsNullOrWhiteSpace(get(x)), () =>
            {
                validator.RuleFor(title!)
                    .Must(t => t!.Trim().Length <= ChatRules.MaxTitleLength)
                    .WithMessage($"'Title' must be at most {ChatRules.MaxTitleLength} characters.");
                validator.RuleFor(title!)
                    .Must(t => !t!.Trim().Any(char.IsControl))
                    .WithMessage("'Title' must not contain control characters (such as line breaks).");
            });
        });
    }
}

/// <summary>
/// Title is optional when creating (null = default title); when given it follows <see cref="ConversationTitleRules"/>.
/// <c>InitialRepositoryId</c> is required only when <paramref name="scope"/> says so (RAG_QUERY_API=multi); without
/// settings (plain unit tests) it is optional.
/// </summary>
public class CreateConversationRequestValidator : AbstractValidator<CreateConversationRequest>
{
    public CreateConversationRequestValidator(ConversationScopeSettings? scope = null)
    {
        this.AddTitleRules(x => x.Title);
        if (scope is { InitialRepositoryRequired: true })
            RuleFor(x => x.InitialRepositoryId).NotNull().WithMessage("'Initial Repository Id' is required.");
    }
}

public class AddScopeRepositoryRequestValidator : AbstractValidator<AddScopeRepositoryRequest>
{
    public AddScopeRepositoryRequestValidator() =>
        RuleFor(x => x.RepositoryId).NotNull().WithMessage("'Repository Id' must not be empty.");
}

/// <summary>Title is required when renaming; it follows <see cref="ConversationTitleRules"/>.</summary>
public class UpdateConversationRequestValidator : AbstractValidator<UpdateConversationRequest>
{
    public UpdateConversationRequestValidator()
    {
        RuleFor(x => x.Title).NotNull().WithMessage("'Title' must not be empty.");
        this.AddTitleRules(x => x.Title);
    }
}

public class AskRequestValidator : AbstractValidator<AskRequest>
{
    public AskRequestValidator()
    {
        RuleFor(x => x.Question).NotEmpty();
        RuleFor(x => x.Question)
            .Must(q => q!.Trim().Length <= ChatRules.MaxQuestionLength)
            .WithMessage($"'Question' must be at most {ChatRules.MaxQuestionLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Question));
        RuleFor(x => x.TopK)
            .InclusiveBetween(1, ChatRules.MaxTopK)
            .When(x => x.TopK is not null);
    }
}
