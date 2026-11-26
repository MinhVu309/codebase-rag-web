using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Rag;

/// <summary>Wire format of RAG Contract v0.3 (snake_case JSON).</summary>
internal static class RagJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal record CreateIndexBody(string RepoUrl, string? Ref, string ClientRef);

internal record CreateIndexResponse(string? IndexId, string? Status);

internal record ProgressDto(int FilesDone, int TotalFiles);

internal record StatsDto(int Files, int Chunks);

internal record ErrorDto(string? Code, string? Message, JsonElement? Details = null);

internal record IndexStatusResponse(
    string? IndexId,
    string? Status,
    string? Stage,
    ProgressDto? Progress,
    string? CommitSha,
    StatsDto? Stats,
    ErrorDto? Error);

internal record QueryOptionsDto(string Mode, bool Rerank);

internal record QueryBody(string IndexId, string Question, int TopK, QueryOptionsDto Options, bool Debug);

internal record CitationDto(
    int RefId,
    string? FilePath,
    string? Symbol,
    string? SymbolKind,
    int StartLine,
    int EndLine,
    string? Snippet,
    double? Score);

internal record QueryResponse(string? Answer, List<CitationDto>? Citations, JsonElement? Usage, int? LatencyMs);

internal record FileResponse(string? Path, int Start, int End, string? Content, string? Language);

internal record ErrorEnvelope(ErrorDto? Error);

// ---- POST /v1/query/multi (Contract v0.3) ----

internal record MultiScopeItemBody(string RepositoryId, string IndexId, string DisplayName);

/// <summary><c>FocusRepositoryIds</c> is null (and therefore not sent) when no Focus was chosen.</summary>
internal record MultiQueryBody(
    string Question,
    List<MultiScopeItemBody> Scope,
    List<string>? FocusRepositoryIds,
    int TopK,
    QueryOptionsDto Options,
    bool Debug);

internal record MultiFocusDto(
    List<string>? RequestedRepositoryIds,
    List<string>? SearchedFocusRepositoryIds,
    List<string>? FallbackRepositoryIds,
    bool FallbackTriggered,
    string? FallbackReason);

internal record MultiOutcomeDto(
    string? RepositoryId,
    string? IndexId,
    string? CommitSha,
    string? Outcome,
    string? Reason,
    int EvidenceCount,
    int? LatencyMs);

internal record MultiCitationDto(
    int RefId,
    string? RepositoryId,
    string? IndexId,
    string? CommitSha,
    string? FilePath,
    string? Symbol,
    string? SymbolKind,
    int StartLine,
    int EndLine,
    string? Snippet,
    double? Score);

internal record MultiQueryResponse(
    string? ExecutionStatus,
    string? AnswerStatus,
    string? Answer,
    MultiFocusDto? Focus,
    List<MultiOutcomeDto>? RepositoryOutcomes,
    List<MultiCitationDto>? Citations,
    JsonElement? Usage,
    int? LatencyMs);
