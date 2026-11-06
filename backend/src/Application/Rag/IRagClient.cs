namespace Application.Rag;

/// <summary>
/// Web Backend's only door to the RAG service (D10/D11). Callers must check the user's access to the
/// project/index BEFORE calling. Every method throws <see cref="RagException"/> on failure.
/// </summary>
public interface IRagClient
{
    /// <summary>GET /v1/health. Never throws; false when RAG is unreachable or unhealthy.</summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);

    /// <summary>POST /v1/indexes. Not retried automatically.</summary>
    Task<RagIndexCreated> CreateIndexAsync(string repoUrl, string gitRef, string clientRef, CancellationToken ct = default);

    /// <summary>GET /v1/indexes/{id}. Retried up to 2 times on transient failures.</summary>
    Task<RagIndexStatus> GetIndexAsync(string indexId, CancellationToken ct = default);

    /// <summary>POST /v1/query. Single-turn (no history). Not retried.</summary>
    Task<RagAnswer> QueryAsync(string indexId, string question, int topK, RagQueryOptions? options = null, CancellationToken ct = default);

    /// <summary>GET /v1/indexes/{id}/files. At most 2000 lines per call.</summary>
    Task<RagFile> GetFileAsync(string indexId, string path, int start, int end, CancellationToken ct = default);

    /// <summary>DELETE /v1/indexes/{id}. Idempotent: an already-missing index is success.</summary>
    Task DeleteIndexAsync(string indexId, CancellationToken ct = default);
}
