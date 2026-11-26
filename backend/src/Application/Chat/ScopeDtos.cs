using Domain.Enums;

namespace Application.Chat;

/// <summary>
/// Conversation Scope settings (D40) the Application layer needs; Infrastructure fills them from configuration.
/// <c>InitialRepositoryRequired</c> is true when RAG_QUERY_API=multi: a new conversation must then say which
/// repository it starts with. It does not change how questions are asked (that still uses the project's active index).
/// </summary>
public record ConversationScopeSettings(int Max, bool InitialRepositoryRequired);

/// <summary>Add one repository to a conversation's Scope.</summary>
public record AddScopeRepositoryRequest(Guid? RepositoryId);

/// <summary>
/// One member of a Scope, as the frontend sees it. The RAG <c>index_id</c> is intentionally absent (D10/D40).
/// <c>DisplayName</c> is "owner/repo".
/// </summary>
public record ScopeRepositoryDto(
    Guid RepositoryId,
    string DisplayName,
    string RepoUrl,
    string Status,
    string? CommitSha,
    DateTimeOffset AddedAt);

/// <summary>Stable machine-readable codes of the Scope endpoints (Contract v2 section 5).</summary>
public static class ScopeErrorCodes
{
    public const string RepositoryNotReady = "REPOSITORY_NOT_READY";
    public const string ScopeLimitReached = "SCOPE_LIMIT_REACHED";
    public const string ScopeCannotBeEmpty = "SCOPE_CANNOT_BE_EMPTY";
}

internal static class ScopeStatus
{
    public static string Format(IndexStatus status) => status.ToString().ToLowerInvariant();
}
