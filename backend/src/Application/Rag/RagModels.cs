using System.Text.Json;
using Domain.Enums;

namespace Application.Rag;

// Shapes follow RAG Contract v0.2 (plan section 0). Replace with the NSwag-generated types once
// contracts/openapi.yaml is pinned; IRagClient keeps the rest of the app insulated from that change.

public record RagIndexCreated(string IndexId, IndexStatus Status);

public record RagIndexError(string Code, string? Message);

public record RagIndexStatus(
    string IndexId,
    IndexStatus Status,
    string? Stage,
    int FilesDone,
    int TotalFiles,
    string? CommitSha,
    int? Files,
    int? Chunks,
    RagIndexError? Error);

public record RagCitation(
    int RefId,
    string FilePath,
    string? Symbol,
    string? SymbolKind,
    int StartLine,
    int EndLine,
    string Snippet,
    double? Score);

public record RagAnswer(string Answer, IReadOnlyList<RagCitation> Citations, JsonElement? Usage, int? LatencyMs);

public record RagFile(string Path, int Start, int End, string Content, string? Language);

/// <summary>Query knobs. Defaults are the MVP baseline: dense retrieval, no rerank.</summary>
public record RagQueryOptions(string Mode = "dense", bool Rerank = false, bool Debug = false);
