namespace Infrastructure.Options;

public enum RagMode
{
    /// <summary>Talk to a real RAG service or a Prism mock at <see cref="RagOptions.BaseUrl"/>.</summary>
    Http,
    /// <summary>In-memory fake, for development while RAG/Prism are not available.</summary>
    Fake,
}

/// <summary>Which RAG query endpoint the chat flow uses (RAG_QUERY_API). D40 rollout switch; J1 does not read it yet.</summary>
public enum RagQueryApi
{
    /// <summary>POST /v1/query on the conversation's single active index (today's behaviour; the rollback path).</summary>
    Single,
    /// <summary>POST /v1/query/multi over the Conversation Scope.</summary>
    Multi,
}

public class RagOptions
{
    public RagMode Mode { get; set; } = RagMode.Http;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>"single" (default) or "multi" (RAG_QUERY_API). Not used by the question flow yet.</summary>
    public RagQueryApi QueryApi { get; set; } = RagQueryApi.Single;

    /// <summary>Retrieval mode sent with every /v1/query: "dense" or "hybrid" (RAG_QUERY_MODE).</summary>
    public string QueryMode { get; set; } = "hybrid";
    /// <summary>Cross-encoder rerank for /v1/query (RAG_QUERY_RERANK). Needs <c>hybrid</c>; adds a lot of latency.</summary>
    public bool QueryRerank { get; set; }

    // Query timeout 65s is derived from the local E2E measurement (docs/reports/e2e-local-2026-11-21.md); all configurable.
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(65);
    public TimeSpan StatusTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Create index, files, delete, health.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Extra attempts for GET /v1/indexes/{id} only (so up to 3 calls in total).</summary>
    public int StatusRetries { get; set; } = 2;
    public TimeSpan StatusRetryDelay { get; set; } = TimeSpan.FromMilliseconds(300);
}

/// <summary>Conversation Scope limits (D40). Not a RAG setting, but RAG enforces the same cap per request.</summary>
public class ConversationScopeOptions
{
    /// <summary>The pinned contract (openapi.yaml v0.3) allows at most 5 scope items per request.</summary>
    public const int ContractMax = 5;

    /// <summary>Most repositories one conversation may hold (CONVERSATION_SCOPE_MAX). Not used yet.</summary>
    public int Max { get; set; } = ContractMax;
}
