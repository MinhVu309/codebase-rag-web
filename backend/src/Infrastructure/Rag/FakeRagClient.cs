using System.Collections.Concurrent;
using Application.Rag;
using Domain.Enums;

namespace Infrastructure.Rag;

/// <summary>
/// In-memory stand-in for the RAG service (RAG_MODE=fake) so Web work is not blocked on R04/Prism.
/// Deterministic: each status poll advances the index one stage. Magic repo URLs: containing
/// "too-large" ends as failed/REPO_TOO_LARGE; containing "invalid" is rejected with INVALID_REPO.
/// </summary>
public class FakeRagClient : IRagClient
{
    private static readonly IndexStatus[] Stages =
        [IndexStatus.Pending, IndexStatus.Cloning, IndexStatus.Parsing, IndexStatus.Embedding, IndexStatus.Ready];

    private const string SampleFile = "src/Program.cs";
    private static readonly string[] SampleLines =
        ["using System;", "", "Console.WriteLine(\"Hello from the fake repository\");"];

    private sealed class FakeIndex(string repoUrl)
    {
        public string RepoUrl { get; } = repoUrl;
        public int Polls;
        public bool TooLarge => RepoUrl.Contains("too-large", StringComparison.OrdinalIgnoreCase);
    }

    private readonly ConcurrentDictionary<string, FakeIndex> _indexes = new();

    public Task<bool> IsHealthyAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<RagIndexCreated> CreateIndexAsync(string repoUrl, string gitRef, string clientRef, CancellationToken ct = default)
    {
        if (repoUrl.Contains("invalid", StringComparison.OrdinalIgnoreCase))
            throw new RagException(RagFailureKind.Invalid, RagErrorCodes.InvalidRepo, 422);

        var id = "fake-" + Guid.NewGuid().ToString("N")[..12];
        _indexes[id] = new FakeIndex(repoUrl);
        return Task.FromResult(new RagIndexCreated(id, IndexStatus.Pending));
    }

    public Task<RagIndexStatus> GetIndexAsync(string indexId, CancellationToken ct = default)
    {
        var index = Find(indexId);
        var polls = Interlocked.Increment(ref index.Polls);

        if (index.TooLarge && polls >= 2)
            return Task.FromResult(new RagIndexStatus(indexId, IndexStatus.Failed, "failed", 0, 0, null, null, null,
                new RagIndexError(RagErrorCodes.RepoTooLarge, "Repository exceeds the size limit")));

        var status = Stages[Math.Min(polls, Stages.Length - 1)];
        var ready = status == IndexStatus.Ready;
        return Task.FromResult(new RagIndexStatus(
            indexId, status, status.ToString().ToLowerInvariant(),
            FilesDone: ready ? 12 : polls * 3, TotalFiles: 12,
            CommitSha: ready ? "0123456789abcdef0123456789abcdef01234567" : null,
            Files: ready ? 12 : null, Chunks: ready ? 48 : null, Error: null));
    }

    public Task<RagAnswer> QueryAsync(string indexId, string question, int topK, RagQueryOptions? options = null, CancellationToken ct = default)
    {
        var index = Find(indexId);
        if (index.Polls < Stages.Length - 1)
            throw new RagException(RagFailureKind.NotReady, RagErrorCodes.IndexNotReady, 409);

        var citation = new RagCitation(1, SampleFile, "Main", "function", 1, SampleLines.Length,
            string.Join('\n', SampleLines), 0.92);
        return Task.FromResult(new RagAnswer(
            "This is a canned answer from the fake RAG service. The entry point prints a greeting [1].",
            [citation], null, 120));
    }

    public Task<RagFile> GetFileAsync(string indexId, string path, int start, int end, CancellationToken ct = default)
    {
        Find(indexId);
        if (start < 1 || end < start || end - start + 1 > 2000)
            throw new RagException(RagFailureKind.Invalid, RagErrorCodes.InvalidRange, 400);
        if (!string.Equals(path, SampleFile, StringComparison.Ordinal))
            throw new RagException(RagFailureKind.NotFound, RagErrorCodes.FileNotFound, 404);

        var from = Math.Min(start, SampleLines.Length);
        var to = Math.Min(end, SampleLines.Length);
        var content = string.Join('\n', SampleLines.Skip(from - 1).Take(to - from + 1));
        return Task.FromResult(new RagFile(path, from, to, content, "csharp"));
    }

    public Task DeleteIndexAsync(string indexId, CancellationToken ct = default)
    {
        _indexes.TryRemove(indexId, out _);
        return Task.CompletedTask;
    }

    private FakeIndex Find(string indexId) =>
        _indexes.TryGetValue(indexId, out var index)
            ? index
            : throw new RagException(RagFailureKind.NotFound, RagErrorCodes.IndexNotFound, 404);
}
