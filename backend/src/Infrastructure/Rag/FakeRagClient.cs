using System.Collections.Concurrent;
using Application.Rag;
using Domain.Enums;

namespace Infrastructure.Rag;

/// <summary>
/// In-memory stand-in for the RAG service (RAG_MODE=fake) so Web work is not blocked on R04/Prism.
/// Deterministic: each status poll advances the index one stage. Magic repo URLs: containing
/// "too-large" ends as failed/REPO_TOO_LARGE; containing "invalid" is rejected with INVALID_REPO.
///
/// <para><b>QueryMultiAsync</b> (POST /v1/query/multi) is deterministic and chosen by a marker inside the question
/// (case-insensitive; if several appear the first of this list wins):
/// <c>fake:unavailable</c> = 503 ALL_INDEXES_UNAVAILABLE; <c>fake:no_evidence</c> = completed/no_evidence;
/// <c>fake:fallback</c> = Focus was not enough, so the other Scope repositories were searched (needs a non-empty Focus and
/// a repository outside it, else the default); <c>fake:partial</c> = one searched repository failed with
/// <c>failed/timeout</c> (needs two searched repositories, else the default). Without a marker: completed/answered with
/// evidence from the first Focus repository (or the first Scope repository when Focus is empty); when Focus is given the
/// rest of the Scope is <c>not_searched/focus_evidence_sufficient</c>.
/// Like the real RAG it answers 422 for an empty or over-sized Scope, duplicate ids, or a Focus outside the Scope. A
/// repository this fake created that is not READY yet is <c>unavailable/index_not_ready</c>; an index it has never
/// seen (e.g. created before an API restart) is treated as ready so dev data survives restarts.</para>
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

    public Task<RagIndexCreated> CreateIndexAsync(string repoUrl, string? gitRef, string clientRef, CancellationToken ct = default)
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

    // ---- POST /v1/query/multi ----

    public const string ScenarioAllUnavailable = "fake:unavailable";
    public const string ScenarioNoEvidence = "fake:no_evidence";
    public const string ScenarioFallback = "fake:fallback";
    public const string ScenarioPartial = "fake:partial";

    /// <summary>The yaml's maxItems for <c>scope</c>.</summary>
    public const int MultiScopeCap = 5;
    public const string FakeCommitSha = "0123456789abcdef0123456789abcdef01234567";

    public Task<RagMultiAnswer> QueryMultiAsync(
        string question,
        IReadOnlyList<RagMultiScopeItem> scope,
        IReadOnlyList<string>? focusRepositoryIds,
        int topK,
        RagQueryOptions? options = null,
        CancellationToken ct = default)
    {
        var focus = focusRepositoryIds ?? [];
        ValidateMulti(scope, focus);

        var scenario = new[] { ScenarioAllUnavailable, ScenarioNoEvidence, ScenarioFallback, ScenarioPartial }
            .FirstOrDefault(m => question.Contains(m, StringComparison.OrdinalIgnoreCase));

        // Per-repository availability: only an index this fake created and has not finished "indexing" is unavailable.
        var available = scope
            .Where(s => scenario != ScenarioAllUnavailable && !(_indexes.TryGetValue(s.IndexId, out var i) && i.Polls < Stages.Length - 1))
            .ToList();
        var outcomes = scope.ToDictionary(s => s.RepositoryId, s => Outcome(s, RagMultiValues.Unavailable, RagMultiValues.IndexNotReady, 0, null));

        if (available.Count == 0)
            throw new RagAllIndexesUnavailableException([.. scope.Select(s => outcomes[s.RepositoryId])], 503);

        var focusItems = available.Where(s => focus.Contains(s.RepositoryId)).ToList();
        var otherItems = available.Except(focusItems).ToList();
        var citations = new List<RagMultiCitation>();
        string answer;
        var answerStatus = "answered";
        var fallbackTriggered = false;
        string? fallbackReason = null;
        var fallbackIds = new List<string>();
        var searchedFocus = focusItems.Select(s => s.RepositoryId).ToList();

        void Found(RagMultiScopeItem s, int evidence, bool cite)
        {
            outcomes[s.RepositoryId] = Outcome(s, RagMultiValues.SearchedWithEvidence, null, evidence, 12);
            if (cite) citations.Add(Cite(citations.Count + 1, s));
        }
        void Empty(RagMultiScopeItem s) =>
            outcomes[s.RepositoryId] = Outcome(s, RagMultiValues.SearchedNoEvidence, null, 0, 12);
        void Triggered(string reason, IEnumerable<RagMultiScopeItem> searched)
        {
            fallbackTriggered = true;
            fallbackReason = reason;
            fallbackIds = [.. searched.Select(s => s.RepositoryId)];
        }

        if (scenario == ScenarioNoEvidence)
        {
            foreach (var s in available) Empty(s);
            if (focus.Count > 0 && otherItems.Count > 0)
                Triggered(focusItems.Count == 0 ? RagMultiValues.FocusedSourcesUnavailable : RagMultiValues.FocusedEvidenceInsufficient, otherItems);
            answerStatus = "no_evidence";
            answer = "The fake RAG service found no evidence for this question in the searched repositories.";
        }
        else if (scenario == ScenarioFallback && focusItems.Count > 0 && otherItems.Count > 0)
        {
            Found(focusItems[0], 1, cite: true);          // 1 chunk < 2: Focus is not sufficient
            foreach (var s in focusItems.Skip(1)) Empty(s);
            Found(otherItems[0], 1, cite: true);
            foreach (var s in otherItems.Skip(1)) Empty(s);
            Triggered(RagMultiValues.FocusedEvidenceInsufficient, otherItems);
            answer = "Canned fallback answer: the focused repository only partly covers this [1]; the rest is in another repository of the conversation [2].";
        }
        else
        {
            // Default (and partial). Focus entirely unavailable -> search the others, like the real fallback.
            var primary = focusItems.Count > 0 ? focusItems : available;
            if (focus.Count > 0 && focusItems.Count == 0)
                Triggered(RagMultiValues.FocusedSourcesUnavailable, available);

            Found(primary[0], 2, cite: true);
            var searched = primary.Skip(1).ToList();
            if (focus.Count == 0 || focusItems.Count == 0) searched = [.. available.Skip(1)];
            foreach (var s in searched) Empty(s);
            foreach (var s in available.Except(searched).Where(s => s != primary[0]))
                outcomes[s.RepositoryId] = Outcome(s, RagMultiValues.NotSearched, RagMultiValues.FocusEvidenceSufficient, 0, null);

            if (scenario == ScenarioPartial && searched.Count > 0)
                outcomes[searched[^1].RepositoryId] = Outcome(searched[^1], RagMultiValues.Failed, RagMultiValues.Timeout, 0, null);

            answer = "This is a canned answer from the fake RAG service. The entry point prints a greeting [1].";
        }

        var ordered = scope.Select(s => outcomes[s.RepositoryId]).ToList();
        var partial = ordered.Any(o => o.Outcome is RagMultiValues.Unavailable or RagMultiValues.Failed);
        return Task.FromResult(new RagMultiAnswer(
            partial ? "partial" : "completed", answerStatus, answer,
            new RagMultiFocus([.. focus], searchedFocus, fallbackIds, fallbackTriggered, fallbackReason),
            ordered, citations, null, 120));
    }

    private static void ValidateMulti(IReadOnlyList<RagMultiScopeItem> scope, IReadOnlyList<string> focus)
    {
        var invalid = scope.Count == 0
                      || scope.Count > MultiScopeCap
                      || scope.Select(s => s.RepositoryId).Distinct().Count() != scope.Count
                      || scope.Select(s => s.IndexId).Distinct().Count() != scope.Count
                      || focus.Distinct().Count() != focus.Count
                      || focus.Any(f => scope.All(s => s.RepositoryId != f));
        if (invalid) throw new RagException(RagFailureKind.Invalid, RagErrorCodes.InvalidRequest, 422);
    }

    private static RagRepositoryOutcome Outcome(RagMultiScopeItem s, string outcome, string? reason, int evidence, int? latencyMs) =>
        new(s.RepositoryId, s.IndexId, FakeCommitSha, outcome, reason, evidence, latencyMs);

    private static RagMultiCitation Cite(int refId, RagMultiScopeItem s) =>
        new(refId, s.RepositoryId, s.IndexId, FakeCommitSha, SampleFile, "Main", "function", 1, SampleLines.Length,
            string.Join('\n', SampleLines), 0.92);

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
