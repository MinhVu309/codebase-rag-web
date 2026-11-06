using Application.Rag;
using Domain.Enums;

namespace Api.Tests.Projects;

/// <summary>Programmable IRagClient for Phase D tests: records calls and can be told to fail.</summary>
public class RecordingRagClient : IRagClient
{
    public List<(string Url, string Ref, string ClientRef)> Created { get; } = [];
    public List<string> Deleted { get; } = [];
    public int GetCalls { get; private set; }
    public List<(string IndexId, string Question, int TopK, RagQueryOptions Options)> Queries { get; } = [];
    public RagException? QueryError { get; set; }

    /// <summary>Answer returned by QueryAsync; defaults to a canned answer with one citation.</summary>
    public Func<string, RagAnswer>? OnQuery { get; set; }

    public RagException? CreateError { get; set; }
    public RagException? GetError { get; set; }
    public RagException? DeleteError { get; set; }

    /// <summary>Status returned by GetIndexAsync; defaults to "embedding".</summary>
    public Func<string, RagIndexStatus>? OnGet { get; set; }

    public Task<bool> IsHealthyAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<RagIndexCreated> CreateIndexAsync(string repoUrl, string gitRef, string clientRef, CancellationToken ct = default)
    {
        if (CreateError is not null) throw CreateError;
        Created.Add((repoUrl, gitRef, clientRef));
        return Task.FromResult(new RagIndexCreated("idx-" + Guid.NewGuid().ToString("N")[..8], IndexStatus.Pending));
    }

    public Task<RagIndexStatus> GetIndexAsync(string indexId, CancellationToken ct = default)
    {
        GetCalls++;
        if (GetError is not null) throw GetError;
        return Task.FromResult(OnGet?.Invoke(indexId) ??
                               new RagIndexStatus(indexId, IndexStatus.Embedding, "embedding", 3, 10, null, null, null, null));
    }

    public Task<RagAnswer> QueryAsync(string indexId, string question, int topK, RagQueryOptions? options = null, CancellationToken ct = default)
    {
        Queries.Add((indexId, question, topK, options ?? new RagQueryOptions()));
        if (QueryError is not null) throw QueryError;
        return Task.FromResult(OnQuery?.Invoke(indexId) ?? DefaultAnswer);
    }

    public static RagAnswer DefaultAnswer { get; } = new(
        "The handler lives in Foo [1] and is wired up in Bar [2].",
        [
            new RagCitation(1, "src/Foo.cs", "Handle", "method", 10, 24, "public void Handle() { }", 0.91),
            new RagCitation(2, "src/Bar.cs", null, null, 3, 5, "services.AddFoo();", 0.74),
        ],
        null, 812);

    public Task<RagFile> GetFileAsync(string indexId, string path, int start, int end, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task DeleteIndexAsync(string indexId, CancellationToken ct = default)
    {
        if (DeleteError is not null) throw DeleteError;
        Deleted.Add(indexId);
        return Task.CompletedTask;
    }

    public static RagIndexStatus Ready(string indexId) =>
        new(indexId, IndexStatus.Ready, "ready", 10, 10, "abc123", 10, 42, null);

    public static RagException Error(RagFailureKind kind, string code) => new(kind, code, 500);
}
