using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Rag;

/// <summary>Wire format of RAG Contract v0.2 (snake_case JSON).</summary>
internal static class RagJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal record CreateIndexBody(string RepoUrl, string Ref, string ClientRef);

internal record CreateIndexResponse(string? IndexId, string? Status);

internal record ProgressDto(int FilesDone, int TotalFiles);

internal record StatsDto(int Files, int Chunks);

internal record ErrorDto(string? Code, string? Message);

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
