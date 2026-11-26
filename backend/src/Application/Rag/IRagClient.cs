namespace Application.Rag;

/// <summary>
/// Web Backend's only door to the RAG service (D10/D11). Callers must check the user's access to the
/// project/index BEFORE calling. Every method throws <see cref="RagException"/> on failure.
/// </summary>
public interface IRagClient
{
    /// <summary>GET /v1/health. Never throws; false when RAG is unreachable or unhealthy.</summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);

    /// <summary>
    /// POST /v1/indexes. Not retried automatically. A null <paramref name="gitRef"/> is not sent, so RAG indexes
    /// the repository's default branch (contract: <c>ref</c> is optional).
    /// </summary>
    Task<RagIndexCreated> CreateIndexAsync(string repoUrl, string? gitRef, string clientRef, CancellationToken ct = default);

    /// <summary>GET /v1/indexes/{id}. Retried up to 2 times on transient failures.</summary>
    Task<RagIndexStatus> GetIndexAsync(string indexId, CancellationToken ct = default);

    /// <summary>POST /v1/query. Single-turn (no history). Not retried.</summary>
    Task<RagAnswer> QueryAsync(string indexId, string question, int topK, RagQueryOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// POST /v1/query/multi (Contract v0.3, D40): one question over a Conversation Scope (1-5 repositories), one fused
    /// answer. <paramref name="scope"/> must already be authorized and resolved server-side; <paramref name="focusRepositoryIds"/>
    /// is an optional soft preference that must be a subset of the scope (RAG answers 422 otherwise); null or empty means
    /// "search the whole scope" and is not sent. Not retried. <c>debug</c> is always false (<paramref name="options"/>.Debug is ignored).
    /// Besides the usual <see cref="RagException"/> kinds, a 503 <c>ALL_INDEXES_UNAVAILABLE</c> throws
    /// <see cref="RagAllIndexesUnavailableException"/> with the per-repository outcomes.
    /// </summary>
    Task<RagMultiAnswer> QueryMultiAsync(
        string question,
        IReadOnlyList<RagMultiScopeItem> scope,
        IReadOnlyList<string>? focusRepositoryIds,
        int topK,
        RagQueryOptions? options = null,
        CancellationToken ct = default);

    /// <summary>GET /v1/indexes/{id}/files. At most 2000 lines per call.</summary>
    Task<RagFile> GetFileAsync(string indexId, string path, int start, int end, CancellationToken ct = default);

    /// <summary>DELETE /v1/indexes/{id}. Idempotent: an already-missing index is success.</summary>
    Task DeleteIndexAsync(string indexId, CancellationToken ct = default);
}
