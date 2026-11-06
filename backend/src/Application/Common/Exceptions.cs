namespace Application.Common;

/// <summary>Base for errors the API maps to a ProblemDetails response.</summary>
public abstract class AppException(string message) : Exception(message);

public sealed class ValidationFailedException(IDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ConflictException(string message) : AppException(message);

public sealed class UnauthorizedException(string message) : AppException(message);

/// <summary>
/// The resource does not exist OR belongs to someone else. The two cases are deliberately
/// indistinguishable so ids of other users' projects/repositories cannot be probed.
/// </summary>
public sealed class NotFoundException(string message) : AppException(message);
