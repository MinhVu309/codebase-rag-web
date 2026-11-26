using Application.Rag;
using Domain.Enums;
using Infrastructure.Rag;

namespace Api.Tests.Rag;

/// <summary>Scenarios of <see cref="FakeRagClient.QueryMultiAsync"/>: deterministic, picked by a marker in the question.</summary>
public class FakeRagClientMultiTests
{
    private readonly FakeRagClient _fake = new();

    private static readonly RagMultiScopeItem A = new("repo-a", "idx_a", "o/a");
    private static readonly RagMultiScopeItem B = new("repo-b", "idx_b", "o/b");
    private static readonly RagMultiScopeItem C = new("repo-c", "idx_c", "o/c");

    private Task<RagMultiAnswer> Ask(string question, RagMultiScopeItem[] scope, params string[] focus) =>
        _fake.QueryMultiAsync(question, scope, focus, 8);

    private static string[] Outcomes(RagMultiAnswer a) => [.. a.RepositoryOutcomes.Select(o => $"{o.RepositoryId}:{o.Outcome}:{o.Reason}")];

    // ---------- default ----------

    [Fact]
    public async Task Default_Is_Completed_Answered_With_A_Citation_From_The_First_Scope_Repository()
    {
        var a = await Ask("how does it start?", [A, B]);

        Assert.Equal(("completed", "answered"), (a.ExecutionStatus, a.AnswerStatus));
        Assert.Equal(["repo-a:searched_with_evidence:", "repo-b:searched_no_evidence:"], Outcomes(a));
        var c = Assert.Single(a.Citations);
        Assert.Equal(("repo-a", "idx_a", 1), (c.RepositoryId, c.IndexId, c.RefId));
        Assert.Contains("[1]", a.Answer);
        Assert.False(a.Focus.FallbackTriggered);
        Assert.Null(a.Focus.FallbackReason);
        Assert.Empty(a.Focus.FallbackRepositoryIds);
        Assert.Empty(a.Focus.RequestedRepositoryIds);
    }

    [Fact]
    public async Task Default_Has_Exactly_One_Outcome_Per_Scope_Repository_In_Scope_Order()
    {
        var a = await Ask("q", [C, A, B]);

        Assert.Equal(["repo-c", "repo-a", "repo-b"], a.RepositoryOutcomes.Select(o => o.RepositoryId));
        Assert.All(a.RepositoryOutcomes, o => Assert.Equal(FakeRagClient.FakeCommitSha, o.CommitSha));
        Assert.Equal(["idx_c", "idx_a", "idx_b"], a.RepositoryOutcomes.Select(o => o.IndexId));
    }

    [Fact]
    public async Task Default_With_Focus_Cites_The_Focus_Repository_And_Does_Not_Search_The_Rest()
    {
        var a = await Ask("q", [A, B, C], "repo-b");

        Assert.Equal(["repo-a:not_searched:focus_evidence_sufficient", "repo-b:searched_with_evidence:", "repo-c:not_searched:focus_evidence_sufficient"], Outcomes(a));
        Assert.Equal("repo-b", Assert.Single(a.Citations).RepositoryId);
        Assert.Equal(["repo-b"], a.Focus.RequestedRepositoryIds);
        Assert.Equal(["repo-b"], a.Focus.SearchedFocusRepositoryIds);
        Assert.False(a.Focus.FallbackTriggered);
        Assert.Equal("completed", a.ExecutionStatus);
    }

    [Fact]
    public async Task Same_Input_Gives_The_Same_Output()
    {
        var first = await Ask("q", [A, B], "repo-a");
        var second = await Ask("q", [A, B], "repo-a");

        Assert.Equal(first.Answer, second.Answer);
        Assert.Equal(first.RepositoryOutcomes, second.RepositoryOutcomes);
        Assert.Equal(first.Citations, second.Citations);
        Assert.Equal(first.LatencyMs, second.LatencyMs);
    }

    // ---------- fake:partial ----------

    [Fact]
    public async Task Partial_Fails_One_Searched_Repository_And_Still_Answers()
    {
        var a = await Ask("what? FAKE:PARTIAL", [A, B]);

        Assert.Equal(("partial", "answered"), (a.ExecutionStatus, a.AnswerStatus));
        Assert.Equal(["repo-a:searched_with_evidence:", "repo-b:failed:timeout"], Outcomes(a));
        Assert.Equal("repo-a", Assert.Single(a.Citations).RepositoryId);
    }

    [Fact]
    public async Task Partial_With_A_Single_Searched_Repository_Behaves_Like_The_Default()
    {
        var a = await Ask("fake:partial", [A, B], "repo-a"); // B is not searched, so it cannot fail

        Assert.Equal("completed", a.ExecutionStatus);
    }

    // ---------- fake:no_evidence ----------

    [Fact]
    public async Task NoEvidence_Searches_Everything_Finds_Nothing_And_Has_No_Citations()
    {
        var a = await Ask("fake:no_evidence", [A, B]);

        Assert.Equal(("completed", "no_evidence"), (a.ExecutionStatus, a.AnswerStatus));
        Assert.Equal(["repo-a:searched_no_evidence:", "repo-b:searched_no_evidence:"], Outcomes(a));
        Assert.Empty(a.Citations);
        Assert.All(a.RepositoryOutcomes, o => Assert.Equal(0, o.EvidenceCount));
    }

    [Fact]
    public async Task NoEvidence_With_Focus_Falls_Back_To_The_Rest_Of_The_Scope_And_Says_So()
    {
        var a = await Ask("fake:no_evidence", [A, B], "repo-a");

        Assert.Equal("no_evidence", a.AnswerStatus);
        Assert.True(a.Focus.FallbackTriggered);
        Assert.Equal("focused_evidence_insufficient", a.Focus.FallbackReason);
        Assert.Equal(["repo-b"], a.Focus.FallbackRepositoryIds);
    }

    // ---------- fake:fallback ----------

    [Fact]
    public async Task Fallback_Uses_The_Non_Focus_Repositories_When_Focus_Is_Not_Enough()
    {
        var a = await Ask("fake:fallback", [A, B, C], "repo-a");

        Assert.Equal(("completed", "answered"), (a.ExecutionStatus, a.AnswerStatus));
        Assert.Equal(["repo-a:searched_with_evidence:", "repo-b:searched_with_evidence:", "repo-c:searched_no_evidence:"], Outcomes(a));
        Assert.Equal(["repo-a"], a.Focus.RequestedRepositoryIds);
        Assert.Equal(["repo-a"], a.Focus.SearchedFocusRepositoryIds);
        Assert.Equal(["repo-b", "repo-c"], a.Focus.FallbackRepositoryIds);
        Assert.True(a.Focus.FallbackTriggered);
        Assert.Equal("focused_evidence_insufficient", a.Focus.FallbackReason);
        Assert.Equal(["repo-a", "repo-b"], a.Citations.Select(c => c.RepositoryId));
        Assert.Equal([1, 2], a.Citations.Select(c => c.RefId));
        Assert.Contains("[2]", a.Answer);
    }

    [Fact]
    public async Task Fallback_Without_Focus_Or_Without_Other_Repositories_Behaves_Like_The_Default()
    {
        var noFocus = await Ask("fake:fallback", [A, B]);
        var allFocused = await Ask("fake:fallback", [A, B], "repo-a", "repo-b");

        Assert.False(noFocus.Focus.FallbackTriggered);
        Assert.False(allFocused.Focus.FallbackTriggered);
    }

    // ---------- fake:unavailable (503) ----------

    [Fact]
    public async Task Unavailable_Throws_The_503_Exception_With_One_Outcome_Per_Repository()
    {
        var ex = await Assert.ThrowsAsync<RagAllIndexesUnavailableException>(() => Ask("fake:unavailable", [A, B]));

        Assert.Equal(RagErrorCodes.AllIndexesUnavailable, ex.Code);
        Assert.Equal(503, ex.UpstreamStatus);
        Assert.Equal(["repo-a", "repo-b"], ex.RepositoryOutcomes.Select(o => o.RepositoryId));
        Assert.All(ex.RepositoryOutcomes, o => Assert.Equal(("unavailable", "index_not_ready"), (o.Outcome, o.Reason)));
    }

    [Fact]
    public async Task Markers_Are_Checked_In_A_Fixed_Order_Unavailable_Wins()
    {
        await Assert.ThrowsAsync<RagAllIndexesUnavailableException>(() => Ask("fake:partial fake:no_evidence fake:unavailable", [A, B]));
        var noEvidence = await Ask("fake:partial fake:no_evidence", [A, B]);
        Assert.Equal("no_evidence", noEvidence.AnswerStatus);
    }

    // ---------- fake indexes that are not ready ----------

    private async Task<string> CreateIndexAsync(bool ready)
    {
        var created = await _fake.CreateIndexAsync("https://github.com/o/r", "main", "c");
        if (ready)
        {
            RagIndexStatus s;
            do { s = await _fake.GetIndexAsync(created.IndexId); } while (s.Status != IndexStatus.Ready);
        }
        return created.IndexId;
    }

    [Fact]
    public async Task A_Fake_Index_That_Is_Still_Indexing_Is_Unavailable_And_The_Result_Is_Partial()
    {
        var ready = new RagMultiScopeItem("repo-ready", await CreateIndexAsync(true), "o/ready");
        var pending = new RagMultiScopeItem("repo-pending", await CreateIndexAsync(false), "o/pending");

        var a = await Ask("q", [ready, pending]);

        Assert.Equal(("partial", "answered"), (a.ExecutionStatus, a.AnswerStatus));
        Assert.Equal(["repo-ready:searched_with_evidence:", "repo-pending:unavailable:index_not_ready"], Outcomes(a));
    }

    [Fact]
    public async Task When_Every_Fake_Index_Is_Still_Indexing_It_Is_A_503()
    {
        var pending = new RagMultiScopeItem("repo-pending", await CreateIndexAsync(false), "o/pending");

        await Assert.ThrowsAsync<RagAllIndexesUnavailableException>(() => Ask("q", [pending]));
    }

    [Fact]
    public async Task A_Focus_Repository_That_Is_Unavailable_Triggers_The_Fallback_To_The_Rest()
    {
        var ready = new RagMultiScopeItem("repo-ready", await CreateIndexAsync(true), "o/ready");
        var pending = new RagMultiScopeItem("repo-pending", await CreateIndexAsync(false), "o/pending");

        var a = await Ask("q", [ready, pending], "repo-pending");

        Assert.True(a.Focus.FallbackTriggered);
        Assert.Equal("focused_sources_unavailable", a.Focus.FallbackReason);
        Assert.Equal(["repo-ready"], a.Focus.FallbackRepositoryIds);
        Assert.Empty(a.Focus.SearchedFocusRepositoryIds);
        Assert.Equal("repo-ready", Assert.Single(a.Citations).RepositoryId);
    }

    // ---------- request validation, as the real RAG does it (422 INVALID_REQUEST) ----------

    [Fact]
    public async Task Invalid_Requests_Are_Rejected_Like_The_Real_RAG()
    {
        var tooMany = Enumerable.Range(1, FakeRagClient.MultiScopeCap + 1)
            .Select(i => new RagMultiScopeItem($"r{i}", $"idx_{i}", $"o/r{i}")).ToArray();

        var cases = new (RagMultiScopeItem[] Scope, string[] Focus)[]
        {
            ([], []),                                   // empty Scope
            (tooMany, []),                              // above the cap of 5
            ([A, A with { IndexId = "idx_x" }], []),    // duplicate repository id
            ([A, B with { IndexId = "idx_a" }], []),    // duplicate index id
            ([A, B], ["repo-z"]),                       // Focus outside the Scope
            ([A, B], ["repo-a", "repo-a"]),             // duplicate Focus id
        };

        foreach (var (scope, focus) in cases)
        {
            var ex = await Assert.ThrowsAsync<RagException>(() => Ask("q", scope, focus));
            Assert.Equal(RagFailureKind.Invalid, ex.Kind);
            Assert.Equal(RagErrorCodes.InvalidRequest, ex.Code);
            Assert.Equal(422, ex.UpstreamStatus);
        }
    }

    [Fact]
    public async Task A_Scope_Of_Exactly_The_Cap_Is_Accepted()
    {
        var scope = Enumerable.Range(1, FakeRagClient.MultiScopeCap)
            .Select(i => new RagMultiScopeItem($"r{i}", $"idx_{i}", $"o/r{i}")).ToArray();

        var a = await Ask("q", scope);

        Assert.Equal(FakeRagClient.MultiScopeCap, a.RepositoryOutcomes.Count);
    }

    [Fact]
    public async Task The_Single_Index_Query_Is_Untouched()
    {
        var id = await CreateIndexAsync(true);

        var answer = await _fake.QueryAsync(id, "q", 5);

        Assert.Contains("[1]", answer.Answer);
        Assert.Equal("src/Program.cs", Assert.Single(answer.Citations).FilePath);
    }
}
