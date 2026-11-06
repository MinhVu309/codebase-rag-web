using System.Text.Json;
using Domain.Entities;

namespace Application.Chat;

public record CreateConversationRequest(string? Title);

/// <summary>A single-turn question (RAG keeps no history). <c>TopK</c> defaults to <see cref="ChatRules.DefaultTopK"/>.</summary>
public record AskRequest(string? Question, int? TopK);

public record ConversationDto(Guid Id, Guid ProjectId, string Title, DateTimeOffset CreatedAt);

public record ConversationSummaryDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    int MessageCount);

/// <summary>
/// Citation snapshot (D14). Stored with the message at answer time so it stays readable after the repository or its
/// RAG index is gone: the frontend shows <c>Snippet</c> (labelled "phiên bản cũ") when the file can no longer be fetched.
/// <c>RepositoryId</c>/<c>CommitSha</c> say which version of which repository the citation came from.
/// </summary>
public record CitationDto(
    int RefId,
    string FilePath,
    string? Symbol,
    string? SymbolKind,
    int StartLine,
    int EndLine,
    string Snippet,
    double? Score,
    Guid RepositoryId,
    string? CommitSha);

public record MessageDto(
    Guid Id,
    Guid ConversationId,
    string Role,
    string Content,
    IReadOnlyList<CitationDto> Citations,
    int? LatencyMs,
    string? ErrorCode,
    DateTimeOffset CreatedAt)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string SerializeCitations(IReadOnlyList<CitationDto> citations) =>
        JsonSerializer.Serialize(citations, Json);

    public static MessageDto From(Message m)
    {
        IReadOnlyList<CitationDto> citations = [];
        if (!string.IsNullOrEmpty(m.Citations))
        {
            try
            {
                citations = JsonSerializer.Deserialize<List<CitationDto>>(m.Citations, Json) ?? [];
            }
            catch (JsonException)
            {
                // A corrupt snapshot must not make the whole history unreadable.
            }
        }

        return new MessageDto(m.Id, m.ConversationId, m.Role.ToString().ToLowerInvariant(), m.Content,
            citations, m.LatencyMs, m.ErrorCode, m.CreatedAt);
    }
}

/// <summary>The two rows created by one question. On a failed turn the assistant message carries only an error code.</summary>
public record AskResultDto(MessageDto UserMessage, MessageDto AssistantMessage);
