using FluentValidation;

namespace Application.Repositories;

/// <summary>
/// Path rules run here, before anything reaches RAG, so a hostile path never leaves the Web Backend. RAG enforces the
/// same boundary again (resolved path must stay inside the snapshot); this is defence in depth, not the only check.
/// Line-range rules live in <see cref="FileRange"/> because they have their own error code.
/// </summary>
public class ReadFileRequestValidator : AbstractValidator<ReadFileRequest>
{
    public ReadFileRequestValidator()
    {
        RuleFor(x => x.Path).NotEmpty();
        When(x => !string.IsNullOrWhiteSpace(x.Path), () =>
        {
            RuleFor(x => x.Path!)
                .MaximumLength(FileRules.MaxPathLength)
                .WithMessage($"'Path' must be at most {FileRules.MaxPathLength} characters.");
            RuleFor(x => x.Path!)
                .Must(p => !p.Split('/', '\\').Contains(".."))
                .WithMessage("'Path' must not contain '..' segments.");
            RuleFor(x => x.Path!)
                .Must(p => !p.StartsWith('/') && !p.StartsWith('\\'))
                .WithMessage("'Path' must be relative to the repository root (no leading '/' or '\\').");
            RuleFor(x => x.Path!)
                .Must(p => !p.Any(char.IsControl))
                .WithMessage("'Path' must not contain control characters.");
        });
    }
}

/// <summary>Resolves and checks the requested line range. Anything invalid is <see cref="Common.InvalidRangeException"/>.</summary>
public static class FileRange
{
    public static (int Start, int End) Resolve(int? start, int? end)
    {
        var s = start ?? 1;
        if (s < 1)
            throw new Common.InvalidRangeException("'start' must be at least 1.");

        long e;
        if (end is null)
        {
            e = Math.Min((long)s + FileRules.MaxLines - 1, int.MaxValue); // default window: up to 2000 lines
        }
        else
        {
            e = end.Value;
            if (e < 1) throw new Common.InvalidRangeException("'end' must be at least 1.");
            if (e < s) throw new Common.InvalidRangeException("'end' must not be before 'start'.");
            if (e - s + 1 > FileRules.MaxLines)
                throw new Common.InvalidRangeException($"At most {FileRules.MaxLines} lines can be requested at once.");
        }

        return (s, (int)e);
    }
}
