using System.Text.Json;
using Domain.Enums;

namespace Application.Rag;

// Shapes follow RAG Contract v0.3 (v0.2 endpoints unchanged) as pinned in contracts/openapi.yaml (see contracts/README.md). The client is
// hand-written (HttpRagClient) and checked against the yaml examples by ContractExampleTests; IRagClient keeps the
// rest of the app insulated if it is ever swapped for a generated one.

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

// ---------------------------------------------------------------------------------------------------------------
// POST /v1/query/multi (Contract v0.3, D40). Ids are strings because that is what the contract carries; the Web
// repository id is a GUID that callers format with ToString("D"). Status/outcome/reason values stay raw strings so a
// value RAG adds later is passed through instead of crashing; the known values are in the constants below.
// ---------------------------------------------------------------------------------------------------------------

/// <summary>One repository the user's Conversation Scope allows RAG to search. <c>IndexId</c> is server-resolved, never from the browser.</summary>
public record RagMultiScopeItem(string RepositoryId, string IndexId, string DisplayName);

/// <summary>How Focus (a soft preference) was applied. Reason is null unless fallback was triggered.</summary>
public record RagMultiFocus(
    IReadOnlyList<string> RequestedRepositoryIds,
    IReadOnlyList<string> SearchedFocusRepositoryIds,
    IReadOnlyList<string> FallbackRepositoryIds,
    bool FallbackTriggered,
    string? FallbackReason);

/// <summary>What happened to one Scope repository. <c>IndexId</c> may be null only inside a 503 error body (the yaml example omits it).</summary>
public record RagRepositoryOutcome(
    string RepositoryId,
    string? IndexId,
    string? CommitSha,
    string Outcome,
    string? Reason,
    int EvidenceCount,
    int? LatencyMs);

/// <summary>A citation with the provenance needed when several repositories can contain the same path.</summary>
public record RagMultiCitation(
    int RefId,
    string RepositoryId,
    string IndexId,
    string? CommitSha,
    string FilePath,
    string? Symbol,
    string? SymbolKind,
    int StartLine,
    int EndLine,
    string Snippet,
    double? Score);

public record RagMultiAnswer(
    string ExecutionStatus,
    string AnswerStatus,
    string Answer,
    RagMultiFocus Focus,
    IReadOnlyList<RagRepositoryOutcome> RepositoryOutcomes,
    IReadOnlyList<RagMultiCitation> Citations,
    JsonElement? Usage,
    int? LatencyMs);

/// <summary>Values defined by openapi.yaml v0.3. Anything else is kept as-is and only logged as a warning.</summary>
public static class RagMultiValues
{
    public static readonly IReadOnlyList<string> ExecutionStatuses = ["completed", "partial"];
    public static readonly IReadOnlyList<string> AnswerStatuses = ["answered", "no_evidence"];

    public const string SearchedWithEvidence = "searched_with_evidence";
    public const string SearchedNoEvidence = "searched_no_evidence";
    public const string Unavailable = "unavailable";
    public const string Failed = "failed";
    public const string NotSearched = "not_searched";
    public static readonly IReadOnlyList<string> Outcomes =
        [SearchedWithEvidence, SearchedNoEvidence, Unavailable, Failed, NotSearched];

    public const string FocusEvidenceSufficient = "focus_evidence_sufficient";
    public const string IndexNotFound = "index_not_found";
    public const string IndexNotReady = "index_not_ready";
    public const string StorageError = "storage_error";
    public const string Timeout = "timeout";
    public const string RetrievalError = "retrieval_error";
    public const string RetrievalCapacity = "retrieval_capacity";
    public static readonly IReadOnlyList<string> Reasons =
        [FocusEvidenceSufficient, IndexNotFound, IndexNotReady, StorageError, Timeout, RetrievalError, RetrievalCapacity];

    public const string FocusedEvidenceInsufficient = "focused_evidence_insufficient";
    public const string FocusedSourcesUnavailable = "focused_sources_unavailable";
    public static readonly IReadOnlyList<string> FallbackReasons = [FocusedEvidenceInsufficient, FocusedSourcesUnavailable];
}
