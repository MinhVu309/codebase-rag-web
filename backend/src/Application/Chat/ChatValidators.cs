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

public class CreateConversationRequestValidator : AbstractValidator<CreateConversationRequest>
{
    public CreateConversationRequestValidator() =>
        RuleFor(x => x.Title)
            .Must(t => t!.Trim().Length > 0).WithMessage("'Title' must not be blank.")
            .Must(t => t!.Trim().Length <= ChatRules.MaxTitleLength)
            .WithMessage($"'Title' must be at most {ChatRules.MaxTitleLength} characters.")
            .When(x => x.Title is not null);
}

public class UpdateConversationRequestValidator : AbstractValidator<UpdateConversationRequest>
{
    public UpdateConversationRequestValidator()
    {
        // Everything below judges the trimmed title, which is also what gets stored.
        RuleFor(x => x.Title)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("'Title' must not be empty.");
        When(x => !string.IsNullOrWhiteSpace(x.Title), () =>
        {
            RuleFor(x => x.Title!)
                .Must(t => t.Trim().Length <= ChatRules.MaxTitleLength)
                .WithMessage($"'Title' must be at most {ChatRules.MaxTitleLength} characters.");
            RuleFor(x => x.Title!)
                .Must(t => !t.Trim().Any(char.IsControl))
                .WithMessage("'Title' must not contain control characters (such as line breaks).");
        });
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
