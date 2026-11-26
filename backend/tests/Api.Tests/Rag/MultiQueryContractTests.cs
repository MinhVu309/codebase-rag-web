using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.Common;
using Application.Rag;
using Infrastructure.Options;
using Infrastructure.Rag;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Tests.Rag;

/// <summary>
/// POST /v1/query/multi against the pinned openapi.yaml v0.3.0 (contracts/README.md). The JSON constants are copied
/// verbatim from the examples in that file; <see cref="ContractExampleTests.Pinned_Contract_Is_Unchanged"/> fails when the
/// yaml changes, which is the cue to re-check them. Everything runs through <see cref="StubHandler"/>: no RAG needed.
/// </summary>
public class MultiQueryContractTests
{
    private const string ApiKey = "super-secret-rag-key";
    private const string Question = "where is the SECRET-QUESTION-TEXT handled?";

    // --- yaml: POST /v1/query/multi, 200 example (partial + fallback) ---
    private const string Example200 = """
    {
      "execution_status": "partial",
      "answer_status": "answered",
      "answer": "API defines the field [1]; client consumes it in the order summary [2].",
      "focus": {
        "requested_repository_ids": ["web-repository-guid-a"],
        "searched_focus_repository_ids": ["web-repository-guid-a"],
        "fallback_repository_ids": ["web-repository-guid-b"],
        "fallback_triggered": true,
        "fallback_reason": "focused_evidence_insufficient"
      },
      "repository_outcomes": [
        {"repository_id": "web-repository-guid-a", "index_id": "idx_backend_a", "commit_sha": "abc123",
         "outcome": "searched_with_evidence", "reason": null, "evidence_count": 2, "latency_ms": 38},
        {"repository_id": "web-repository-guid-b", "index_id": "idx_client_b", "commit_sha": "def456",
         "outcome": "searched_with_evidence", "reason": null, "evidence_count": 1, "latency_ms": 42}
      ],
      "citations": [
        {"ref_id": 1, "repository_id": "web-repository-guid-a", "index_id": "idx_backend_a", "commit_sha": "abc123",
         "file_path": "app/routes/orders.py", "symbol": "get_order", "symbol_kind": "function",
         "start_line": 20, "end_line": 42, "snippet": "...", "score": 0.91}
      ],
      "usage": null,
      "latency_ms": 920,
      "debug": null
    }
    """;

    // --- yaml: 503 example (note: outcomes carry only repository_id, outcome, reason) ---
    private const string Example503 = """
    {"error": {"code": "ALL_INDEXES_UNAVAILABLE", "message": "No supplied index is available for querying",
      "details": {"repository_outcomes": [
        {"repository_id": "web-repository-guid-a", "outcome": "unavailable", "reason": "index_not_ready"}]}}}
    """;

    // --- yaml: shared responses / 502 / 504 ---
    private const string Example401 = """{"error":{"code":"UNAUTHORIZED","message":"Missing or invalid API key","details":{}}}""";
    private const string Example422 = """{"error":{"code":"INVALID_REQUEST","message":"Validation failed","details":{"field":"question","reason":"exceeds maximum length of 2000 characters"}}}""";
    private const string Example500 = """{"error":{"code":"INTERNAL_ERROR","message":"Unexpected server error","details":{}}}""";
    private const string Example502 = """{"error":{"code":"LLM_ERROR","message":"The language model request failed","details":{}}}""";
    private const string Example504 = """{"error":{"code":"TIMEOUT","message":"The request timed out","details":{}}}""";

    private static readonly RagMultiScopeItem RepoA = new("web-repository-guid-a", "idx_backend_a", "orders-api");
    private static readonly RagMultiScopeItem RepoB = new("web-repository-guid-b", "idx_client_b", "orders-client");
    private static readonly RagMultiScopeItem[] Scope = [RepoA, RepoB];

    private readonly CapturingLoggerProvider _logs = new();

    private HttpRagClient CreateClient(StubHandler handler, Action<RagOptions>? configure = null, IHttpContextAccessor? accessor = null)
    {
        var options = new RagOptions { BaseUrl = "http://rag.test:4010", ApiKey = ApiKey, StatusRetryDelay = TimeSpan.Zero };
        configure?.Invoke(options);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://rag.test:4010/"), Timeout = Timeout.InfiniteTimeSpan };
        var logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs)).CreateLogger<HttpRagClient>();
        return new HttpRagClient(http, Options.Create(options), logger, accessor);
    }

    private static StubHandler Ok(string json = Example200) => new StubHandler().Respond(HttpStatusCode.OK, json);

    private static JsonNode Request(StubHandler handler) => JsonNode.Parse(handler.Requests.Single().Body!)!;

    /// <summary>The 200 example after <paramref name="edit"/> has changed it.</summary>
    private static string With(Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(Example200)!;
        edit(node);
        return node.ToJsonString();
    }

    // ---------- the 200 example ----------

    [Fact]
    public async Task Example_200_Partial_With_Fallback_Parses_Every_Field()
    {
        var a = await CreateClient(Ok()).QueryMultiAsync(Question, Scope, [RepoA.RepositoryId], 8);

        Assert.Equal("partial", a.ExecutionStatus);
        Assert.Equal("answered", a.AnswerStatus);
        Assert.Equal("API defines the field [1]; client consumes it in the order summary [2].", a.Answer);
        Assert.Equal(920, a.LatencyMs);
        Assert.Null(a.Usage); // "usage": null

        Assert.Equal(["web-repository-guid-a"], a.Focus.RequestedRepositoryIds);
        Assert.Equal(["web-repository-guid-a"], a.Focus.SearchedFocusRepositoryIds);
        Assert.Equal(["web-repository-guid-b"], a.Focus.FallbackRepositoryIds);
        Assert.True(a.Focus.FallbackTriggered);
        Assert.Equal("focused_evidence_insufficient", a.Focus.FallbackReason);

        Assert.Equal(2, a.RepositoryOutcomes.Count);
        Assert.Equal(new RagRepositoryOutcome("web-repository-guid-a", "idx_backend_a", "abc123",
            "searched_with_evidence", null, 2, 38), a.RepositoryOutcomes[0]);
        Assert.Equal(new RagRepositoryOutcome("web-repository-guid-b", "idx_client_b", "def456",
            "searched_with_evidence", null, 1, 42), a.RepositoryOutcomes[1]);

        var c = Assert.Single(a.Citations);
        Assert.Equal(new RagMultiCitation(1, "web-repository-guid-a", "idx_backend_a", "abc123", "app/routes/orders.py",
            "get_order", "function", 20, 42, "...", 0.91), c);
    }

    [Fact]
    public async Task Nulls_Are_Kept_Where_The_Contract_Allows_Them()
    {
        var json = With(n =>
        {
            n["answer_status"] = "no_evidence";
            n["focus"]!["fallback_triggered"] = false;
            n["focus"]!["fallback_reason"] = null;
            n["focus"]!["fallback_repository_ids"] = new JsonArray();
            n["repository_outcomes"]![0]!["commit_sha"] = null;
            n["repository_outcomes"]![1]!["latency_ms"] = null;
            n["citations"]![0]!["commit_sha"] = null;
            n["citations"]![0]!["symbol"] = null;
            n["citations"]![0]!["symbol_kind"] = null;
            n["usage"] = JsonNode.Parse("""{"input_tokens":120,"output_tokens":null}""");
        });

        var a = await CreateClient(Ok(json)).QueryMultiAsync(Question, Scope, null, 8);

        Assert.Equal("no_evidence", a.AnswerStatus);
        Assert.False(a.Focus.FallbackTriggered);
        Assert.Null(a.Focus.FallbackReason);
        Assert.Empty(a.Focus.FallbackRepositoryIds);
        Assert.Null(a.RepositoryOutcomes[0].CommitSha);
        Assert.Null(a.RepositoryOutcomes[1].LatencyMs);
        Assert.Null(a.Citations[0].CommitSha);
        Assert.Null(a.Citations[0].Symbol);
        Assert.Null(a.Citations[0].SymbolKind);
        Assert.Equal(120, a.Usage!.Value.GetProperty("input_tokens").GetInt32());
        Assert.Equal(JsonValueKind.Null, a.Usage.Value.GetProperty("output_tokens").ValueKind);
    }

    [Fact]
    public async Task Debug_Block_In_The_Response_Is_Ignored()
    {
        var json = With(n => n["debug"] = JsonNode.Parse("""{"candidate_count":3,"fused_chunk_count":2,"context_source_count":2,"selected_evidence":[]}"""));

        var a = await CreateClient(Ok(json)).QueryMultiAsync(Question, Scope, null, 8);

        Assert.Equal("partial", a.ExecutionStatus);
    }

    // ---------- the request ----------

    [Fact]
    public async Task Request_Matches_The_Yaml_Example_In_Snake_Case_With_Headers()
    {
        var handler = Ok();

        await CreateClient(handler).QueryMultiAsync("Trace how an order response is produced and consumed.", Scope,
            [RepoA.RepositoryId], 8, new RagQueryOptions("hybrid", false));

        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/v1/query/multi", req.Uri.AbsolutePath);
        Assert.Equal(ApiKey, req.Headers["X-API-Key"]);
        Assert.False(string.IsNullOrWhiteSpace(req.Headers["X-Request-ID"]));

        var root = Request(handler);
        Assert.Equal("Trace how an order response is produced and consumed.", (string?)root["question"]);
        Assert.Equal(2, root["scope"]!.AsArray().Count);
        Assert.Equal("web-repository-guid-a", (string?)root["scope"]![0]!["repository_id"]);
        Assert.Equal("idx_backend_a", (string?)root["scope"]![0]!["index_id"]);
        Assert.Equal("orders-api", (string?)root["scope"]![0]!["display_name"]);
        Assert.Equal("web-repository-guid-b", (string?)root["scope"]![1]!["repository_id"]);
        Assert.Equal(["web-repository-guid-a"], root["focus_repository_ids"]!.AsArray().Select(x => (string?)x));
        Assert.Equal(8, (int?)root["top_k"]);
        Assert.Equal("hybrid", (string?)root["options"]!["mode"]);
        Assert.False((bool)root["options"]!["rerank"]!);
        Assert.False((bool)root["debug"]!);
    }

    [Fact]
    public async Task Request_Uses_Only_Snake_Case_Property_Names()
    {
        var handler = Ok();
        await CreateClient(handler).QueryMultiAsync(Question, Scope, [RepoB.RepositoryId], 5);

        static IEnumerable<string> Names(JsonNode? n) => n switch
        {
            JsonObject o => o.SelectMany(p => Names(p.Value).Prepend(p.Key)),
            JsonArray a => a.SelectMany(Names),
            _ => [],
        };
        var names = Names(Request(handler)).Distinct().ToList();

        Assert.All(names, name => Assert.Matches("^[a-z]+(_[a-z]+)*$", name));
        Assert.Contains("focus_repository_ids", names);
        Assert.Contains("display_name", names);
    }

    // Focus is OMITTED when empty. openapi.yaml allows both ("Empty or omitted means search all Scope"); omitting is the
    // smaller payload and does not depend on how a validator treats an empty array.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_Focus_Is_Not_Sent(bool focusIsNull)
    {
        string[]? focus = focusIsNull ? null : [];
        var handler = Ok();

        await CreateClient(handler).QueryMultiAsync(Question, Scope, focus, 8);

        Assert.False(((JsonObject)Request(handler)).ContainsKey("focus_repository_ids"));
    }

    [Fact]
    public async Task Debug_Is_Always_False_Even_When_The_Options_Ask_For_It()
    {
        var handler = Ok();

        await CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8, new RagQueryOptions("dense", false, Debug: true));

        Assert.False((bool)Request(handler)["debug"]!);
    }

    [Theory]
    [InlineData("dense", false)]
    [InlineData("hybrid", false)]
    [InlineData("hybrid", true)]
    public async Task Options_Mode_And_Rerank_Are_Forwarded_As_Given(string mode, bool rerank)
    {
        var handler = Ok();

        await CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8, new RagQueryOptions(mode, rerank));

        Assert.Equal(mode, (string?)Request(handler)["options"]!["mode"]);
        Assert.Equal(rerank, (bool)Request(handler)["options"]!["rerank"]!);
    }

    [Fact]
    public async Task Request_Id_Of_The_Current_Web_Request_Is_Forwarded()
    {
        var context = new DefaultHttpContext();
        context.Items[RequestIds.HeaderName] = "web-req-123";
        var handler = Ok();

        await CreateClient(handler, accessor: new HttpContextAccessor { HttpContext = context })
            .QueryMultiAsync(Question, Scope, null, 8);

        Assert.Equal("web-req-123", handler.Requests.Single().Headers["X-Request-ID"]);
    }

    [Fact]
    public async Task Empty_Scope_Is_Rejected_Before_Any_Call()
    {
        var handler = Ok();

        await Assert.ThrowsAsync<ArgumentException>(() => CreateClient(handler).QueryMultiAsync(Question, [], null, 8));

        Assert.Empty(handler.Requests);
    }

    // ---------- 503 ALL_INDEXES_UNAVAILABLE ----------

    [Fact]
    public async Task Example_503_Throws_The_Dedicated_Exception_With_The_Outcomes()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.ServiceUnavailable, Example503);

        var ex = await Assert.ThrowsAsync<RagAllIndexesUnavailableException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.Equal(RagErrorCodes.AllIndexesUnavailable, ex.Code);
        Assert.Equal(503, ex.UpstreamStatus);
        Assert.IsAssignableFrom<RagException>(ex); // existing catch (RagException) blocks still see it
        // The yaml example has no index_id/commit_sha/evidence_count/latency_ms: those stay null/0.
        Assert.Equal([new RagRepositoryOutcome("web-repository-guid-a", null, null, "unavailable", "index_not_ready", 0, null)],
            ex.RepositoryOutcomes);
    }

    [Fact]
    public async Task A_503_With_Full_Outcomes_Keeps_Every_Field()
    {
        var body = """
        {"error":{"code":"ALL_INDEXES_UNAVAILABLE","message":"none","details":{"repository_outcomes":[
          {"repository_id":"r1","index_id":"idx_1","commit_sha":"c1","outcome":"unavailable","reason":"index_not_found","evidence_count":0,"latency_ms":3},
          {"repository_id":"r2","index_id":"idx_2","commit_sha":null,"outcome":"failed","reason":"retrieval_capacity","evidence_count":0,"latency_ms":null}]}}}
        """;
        var handler = new StubHandler().Respond(HttpStatusCode.ServiceUnavailable, body);

        var ex = await Assert.ThrowsAsync<RagAllIndexesUnavailableException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.Equal(
        [
            new RagRepositoryOutcome("r1", "idx_1", "c1", "unavailable", "index_not_found", 0, 3),
            new RagRepositoryOutcome("r2", "idx_2", null, "failed", "retrieval_capacity", 0, null),
        ], ex.RepositoryOutcomes);
    }

    [Theory]
    [InlineData("""{"error":{"code":"ALL_INDEXES_UNAVAILABLE","message":"none","details":{}}}""")]
    [InlineData("""{"error":{"code":"ALL_INDEXES_UNAVAILABLE","message":"none","details":null}}""")]
    [InlineData("""{"error":{"code":"ALL_INDEXES_UNAVAILABLE","message":"none"}}""")]
    [InlineData("""{"error":{"code":"ALL_INDEXES_UNAVAILABLE","message":"none","details":{"repository_outcomes":"nope"}}}""")]
    [InlineData("""{"error":{"code":"ALL_INDEXES_UNAVAILABLE","message":"none","details":{"repository_outcomes":[{"outcome":"unavailable"},42,{"repository_id":"r1","outcome":"unavailable","evidence_count":"x"}]}}}""")]
    public async Task A_503_With_Missing_Or_Malformed_Details_Still_Throws_With_No_Usable_Outcomes(string body)
    {
        var handler = new StubHandler().Respond(HttpStatusCode.ServiceUnavailable, body);

        var ex = await Assert.ThrowsAsync<RagAllIndexesUnavailableException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.Empty(ex.RepositoryOutcomes);
    }

    [Fact]
    public async Task A_503_Html_From_A_Proxy_Is_A_Plain_Unavailable_Not_The_Dedicated_Exception()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.ServiceUnavailable, "<html><body>503 Service Unavailable</body></html>");

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.IsNotType<RagAllIndexesUnavailableException>(ex);
        Assert.Equal(RagFailureKind.Unavailable, ex.Kind);
        Assert.Equal(RagErrorCodes.Unavailable, ex.Code);
    }

    // ---------- other errors: the existing mapper's classification, one request, no retry ----------

    [Theory]
    [InlineData(401, Example401, RagFailureKind.Unauthorized, "UNAUTHORIZED")]
    [InlineData(422, Example422, RagFailureKind.Invalid, "INVALID_REQUEST")]
    [InlineData(500, Example500, RagFailureKind.Internal, "INTERNAL_ERROR")]
    [InlineData(502, Example502, RagFailureKind.LlmError, "LLM_ERROR")]
    [InlineData(504, Example504, RagFailureKind.Timeout, "TIMEOUT")]
    public async Task Error_Examples_Map_To_The_Existing_Failure_Kinds_And_Are_Never_Retried(int status, string body, RagFailureKind kind, string code)
    {
        var handler = new StubHandler().Respond((HttpStatusCode)status, body);

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.IsNotType<RagAllIndexesUnavailableException>(ex);
        Assert.Equal(kind, ex.Kind);
        Assert.Equal(code, ex.Code);
        Assert.Equal(status, ex.UpstreamStatus);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(504)]
    public async Task Error_With_An_Html_Body_Falls_Back_To_The_Status(int status)
    {
        var handler = new StubHandler().Respond((HttpStatusCode)status, "<!doctype html><html><body>Bad gateway</body></html>");

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.Equal(status switch { 502 => RagFailureKind.LlmError, 504 => RagFailureKind.Timeout, _ => RagFailureKind.Internal }, ex.Kind);
        Assert.Equal(status, ex.UpstreamStatus);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_200_With_An_Html_Body_Is_An_Invalid_Response()
    {
        var handler = Ok("<html>hello</html>");

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.Equal(RagFailureKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public async Task Connection_Failure_Is_Unavailable_And_Not_Retried()
    {
        var handler = new StubHandler().Throw(new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.Equal(RagFailureKind.Unavailable, ex.Kind);
        Assert.Single(handler.Requests);
    }

    // The query timeout (RAG_QUERY_TIMEOUT_SECONDS, default 65s) is the one that applies, not the 5s status or 10s request ones.
    [Fact]
    public async Task Client_Side_Timeout_Uses_The_Query_Timeout_And_Is_Not_Retried()
    {
        var handler = new StubHandler().Hang();
        var client = CreateClient(handler, o =>
        {
            o.QueryTimeout = TimeSpan.FromMilliseconds(100);
            o.StatusTimeout = TimeSpan.FromMinutes(5);
            o.RequestTimeout = TimeSpan.FromMinutes(5);
            o.StatusRetries = 5;
        });

        var ex = await Assert.ThrowsAsync<RagException>(() => client.QueryMultiAsync(Question, Scope, null, 8));

        Assert.Equal(RagFailureKind.Timeout, ex.Kind);
        Assert.Equal(RagErrorCodes.Timeout, ex.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void The_Default_Query_Timeout_Is_65_Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(65), new RagOptions().QueryTimeout);
    }

    [Fact]
    public async Task Caller_Cancellation_Is_Not_Reported_As_A_RAG_Timeout()
    {
        var handler = new StubHandler().Hang();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8, null, cts.Token));
    }

    // ---------- malformed 200 bodies ----------

    [Theory]
    [InlineData("answer")]
    [InlineData("execution_status")]
    [InlineData("answer_status")]
    [InlineData("focus")]
    [InlineData("repository_outcomes")]
    public async Task A_200_Missing_A_Required_Field_Is_An_Invalid_Response(string field)
    {
        var handler = Ok(With(n => ((JsonObject)n).Remove(field)));

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.Equal(RagFailureKind.InvalidResponse, ex.Kind);
    }

    [Theory]
    [InlineData("repository_id")]
    [InlineData("index_id")]
    public async Task A_Citation_Without_Provenance_Is_An_Invalid_Response(string field)
    {
        var handler = Ok(With(n => n["citations"]![0]!.AsObject().Remove(field)));

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.Equal(RagFailureKind.InvalidResponse, ex.Kind);
    }

    [Theory]
    [InlineData("repository_id")]
    [InlineData("outcome")]
    public async Task An_Outcome_Without_Its_Key_Fields_Is_An_Invalid_Response(string field)
    {
        var handler = Ok(With(n => n["repository_outcomes"]![0]!.AsObject().Remove(field)));

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8));

        Assert.Equal(RagFailureKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public async Task Missing_Citations_Mean_None()
    {
        var handler = Ok(With(n => ((JsonObject)n).Remove("citations")));

        var a = await CreateClient(handler).QueryMultiAsync(Question, Scope, null, 8);

        Assert.Empty(a.Citations);
    }

    // ---------- enums: known values parse quietly, unknown ones are kept and only warned about ----------

    [Theory]
    [InlineData("searched_with_evidence", null)]
    [InlineData("searched_no_evidence", null)]
    [InlineData("not_searched", "focus_evidence_sufficient")]
    [InlineData("unavailable", "index_not_found")]
    [InlineData("unavailable", "index_not_ready")]
    [InlineData("unavailable", "storage_error")]
    [InlineData("failed", "timeout")]
    [InlineData("failed", "retrieval_error")]
    [InlineData("failed", "retrieval_capacity")]
    public async Task Every_Outcome_And_Reason_In_The_Yaml_Parses_Without_A_Warning(string outcome, string? reason)
    {
        var json = With(n =>
        {
            n["repository_outcomes"]![0]!["outcome"] = outcome;
            n["repository_outcomes"]![0]!["reason"] = reason;
        });

        var a = await CreateClient(Ok(json)).QueryMultiAsync(Question, Scope, null, 8);

        Assert.Equal(outcome, a.RepositoryOutcomes[0].Outcome);
        Assert.Equal(reason, a.RepositoryOutcomes[0].Reason);
        Assert.DoesNotContain(_logs.Entries, e => e.Contains("unrecognised"));
    }

    [Theory]
    [InlineData("focused_evidence_insufficient")]
    [InlineData("focused_sources_unavailable")]
    public async Task Every_Fallback_Reason_In_The_Yaml_Parses_Without_A_Warning(string reason)
    {
        var a = await CreateClient(Ok(With(n => n["focus"]!["fallback_reason"] = reason))).QueryMultiAsync(Question, Scope, null, 8);

        Assert.Equal(reason, a.Focus.FallbackReason);
        Assert.DoesNotContain(_logs.Entries, e => e.Contains("unrecognised"));
    }

    [Theory]
    [InlineData("completed", "answered")]
    [InlineData("completed", "no_evidence")]
    [InlineData("partial", "answered")]
    [InlineData("partial", "no_evidence")]
    public async Task Every_Status_Combination_In_The_Yaml_Parses_Without_A_Warning(string execution, string answer)
    {
        var json = With(n => { n["execution_status"] = execution; n["answer_status"] = answer; });

        var a = await CreateClient(Ok(json)).QueryMultiAsync(Question, Scope, null, 8);

        Assert.Equal((execution, answer), (a.ExecutionStatus, a.AnswerStatus));
        Assert.DoesNotContain(_logs.Entries, e => e.Contains("unrecognised"));
    }

    [Fact]
    public async Task Unknown_Enum_Values_Do_Not_Crash_The_Client_They_Are_Kept_And_Warned_About()
    {
        var json = With(n =>
        {
            n["execution_status"] = "degraded";
            n["answer_status"] = "needs_clarification";
            n["focus"]!["fallback_reason"] = "something_new";
            n["repository_outcomes"]![0]!["outcome"] = "rate_limited";
            n["repository_outcomes"]![0]!["reason"] = "quota";
        });

        var a = await CreateClient(Ok(json)).QueryMultiAsync(Question, Scope, null, 8);

        Assert.Equal("degraded", a.ExecutionStatus);
        Assert.Equal("needs_clarification", a.AnswerStatus);
        Assert.Equal("something_new", a.Focus.FallbackReason);
        Assert.Equal("rate_limited", a.RepositoryOutcomes[0].Outcome);
        Assert.Equal("quota", a.RepositoryOutcomes[0].Reason);
        var warnings = string.Join("\n", _logs.Entries);
        foreach (var field in new[] { "execution_status", "answer_status", "fallback_reason", "outcome", "reason" })
            Assert.Contains(field, warnings);
        Assert.DoesNotContain("SECRET-QUESTION-TEXT", warnings);
        Assert.DoesNotContain("API defines the field", warnings); // the answer text is never logged
    }

    [Fact]
    public async Task The_Known_Value_Lists_Match_The_Yaml_Enums()
    {
        // Guards RagMultiValues against drifting from the pinned yaml (the pin test covers the file itself).
        var yaml = await File.ReadAllTextAsync(Path.Combine(ContractsDirPath(), "openapi.yaml"));

        static string[] EnumOf(string yaml, string property, string schema)
        {
            var schemaStart = yaml.IndexOf($"\n    {schema}:\n", StringComparison.Ordinal);
            Assert.True(schemaStart >= 0, $"schema {schema} not found");
            var m = Regex.Match(yaml[schemaStart..], $@"\n        {property}:\r?\n(?:          .*\r?\n)*?          enum:(?: \[(?<inline>[^\]]*)\]|\r?\n(?<block>(?:            - .*\r?\n)+))");
            Assert.True(m.Success, $"{schema}.{property}.enum not found");
            var raw = m.Groups["inline"].Success ? m.Groups["inline"].Value.Split(',') : m.Groups["block"].Value.Split('\n');
            return raw.Select(x => x.Trim().TrimStart('-').Trim()).Where(x => x.Length > 0 && x != "null").ToArray();
        }

        Assert.Equal(EnumOf(yaml, "outcome", "MultiRepositoryOutcome").Order(), RagMultiValues.Outcomes.Order());
        Assert.Equal(EnumOf(yaml, "reason", "MultiRepositoryOutcome").Order(), RagMultiValues.Reasons.Order());
        Assert.Equal(EnumOf(yaml, "fallback_reason", "MultiQueryFocus").Order(), RagMultiValues.FallbackReasons.Order());
        Assert.Equal(EnumOf(yaml, "execution_status", "MultiQueryResponse").Order(), RagMultiValues.ExecutionStatuses.Order());
        Assert.Equal(EnumOf(yaml, "answer_status", "MultiQueryResponse").Order(), RagMultiValues.AnswerStatuses.Order());
    }

    private static string ContractsDirPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "contracts");
            if (File.Exists(Path.Combine(candidate, "openapi.yaml"))) return candidate;
        }
        throw new FileNotFoundException("contracts/openapi.yaml not found above " + AppContext.BaseDirectory);
    }

    // ---------- logging (D22) ----------

    [Fact]
    public async Task Question_Answer_Names_Paths_Index_Ids_And_The_Key_Are_Never_Logged()
    {
        await CreateClient(Ok()).QueryMultiAsync(Question, Scope, [RepoA.RepositoryId], 8);

        var failing = new StubHandler().Respond(HttpStatusCode.ServiceUnavailable,
            """{"error":{"code":"ALL_INDEXES_UNAVAILABLE","message":"nothing for SECRET-QUESTION-TEXT","details":{"repository_outcomes":[{"repository_id":"web-repository-guid-a","index_id":"idx_backend_a","outcome":"unavailable","reason":"index_not_ready"}]}}}""");
        await Assert.ThrowsAsync<RagAllIndexesUnavailableException>(() => CreateClient(failing).QueryMultiAsync(Question, Scope, null, 8));

        var unreachable = new StubHandler().Throw(new HttpRequestException($"cannot reach with {ApiKey}"));
        await Assert.ThrowsAsync<RagException>(() => CreateClient(unreachable).QueryMultiAsync(Question, Scope, null, 8));

        var logs = string.Join("\n", _logs.Entries);
        Assert.Contains("v1/query/multi", logs); // something was logged, so the assertions below mean something
        foreach (var secret in new[]
                 {
                     "SECRET-QUESTION-TEXT", ApiKey, "API defines the field", "app/routes/orders.py", "get_order",
                     "idx_backend_a", "idx_client_b", "orders-api", "orders-client",
                 })
            Assert.DoesNotContain(secret, logs);
    }
}
