namespace Infrastructure.Options;

public enum RagMode
{
    /// <summary>Talk to a real RAG service or a Prism mock at <see cref="RagOptions.BaseUrl"/>.</summary>
    Http,
    /// <summary>In-memory fake, for development while RAG/Prism are not available.</summary>
    Fake,
}

public class RagOptions
{
    public RagMode Mode { get; set; } = RagMode.Http;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    // Final values are decided from measured p95 at the end of Sprint 4 (plan section 0); all configurable.
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan StatusTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Create index, files, delete, health.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Extra attempts for GET /v1/indexes/{id} only (so up to 3 calls in total).</summary>
    public int StatusRetries { get; set; } = 2;
    public TimeSpan StatusRetryDelay { get; set; } = TimeSpan.FromMilliseconds(300);
}
