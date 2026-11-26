using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Middleware;
using Application.Rag;
using Domain.Enums;
using Infrastructure.Options;
using Infrastructure.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Api.Tests.Rag;

/// <summary>
/// Contract tests against the pinned <c>contracts/openapi.yaml</c> (see contracts/README.md). The JSON below is copied
/// verbatim from the examples in that file and fed through <see cref="HttpRagClient"/> via <see cref="StubHandler"/>.
/// No YAML parser is used: <see cref="Pinned_Contract_Is_Unchanged"/> fails when the yaml changes, which is the cue to
/// re-check these samples. The yaml is Prism's source too, so these are the same payloads the mock serves.
/// </summary>
public class ContractExampleTests
{
    // Keep in sync with contracts/README.md and BACKEND_PLAN.md section 0.
    private const string PinnedSha256 = "5f397fa87fb1fe2444f17d6fb4d7f033c2b1584a6ff7fed44d6e344daaadc606";
    private const string PinnedCommit = "195e2a967509a2a2b6682604522cd071b7151ca1";

    private static HttpRagClient CreateClient(StubHandler handler)
    {
        var options = new RagOptions { BaseUrl = "http://rag.test:4010", ApiKey = "test-key", StatusRetryDelay = TimeSpan.Zero };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://rag.test:4010/"), Timeout = Timeout.InfiniteTimeSpan };
        return new HttpRagClient(http, Options.Create(options), NullLogger<HttpRagClient>.Instance);
    }

    private static string ContractsDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "contracts");
            if (File.Exists(Path.Combine(candidate, "openapi.yaml"))) return candidate;
        }
        throw new FileNotFoundException("contracts/openapi.yaml not found above " + AppContext.BaseDirectory);
    }

    // ---------- the pin itself ----------

    [Fact]
    public void Pinned_Contract_Is_Unchanged()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ContractsDir(), "openapi.yaml"));

        Assert.Equal(PinnedSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public void Contracts_Readme_Records_Source_Commit_Version_And_Sha()
    {
        var readme = File.ReadAllText(Path.Combine(ContractsDir(), "README.md"));

        Assert.Contains(PinnedSha256, readme);
        Assert.Contains(PinnedCommit, readme);
        Assert.Contains("0.3.0", readme);
    }

    [Fact]
    public void Every_Error_Code_In_The_Contract_Is_Known_To_The_Client()
    {
        var yaml = File.ReadAllText(Path.Combine(ContractsDir(), "openapi.yaml"));
        var block = Regex.Match(yaml, @"^    ErrorCode:\r?\n(?:.*\r?\n)*?      enum:\r?\n((?:        - \w+\r?\n)+)", RegexOptions.Multiline);
        Assert.True(block.Success, "could not find components.schemas.ErrorCode.enum in openapi.yaml");
        var contractCodes = Regex.Matches(block.Groups[1].Value, @"- (\w+)").Select(m => m.Groups[1].Value).ToHashSet();

        var clientCodes = typeof(RagErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Except([RagErrorCodes.Unavailable, RagErrorCodes.InvalidResponse]) // raised by the client, not by RAG
            .ToHashSet();

        Assert.Equal(contractCodes.Order(), clientCodes.Order());
    }

    // ---------- requests ----------

    [Fact]
    public async Task CreateIndex_Example_Request_And_Response()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.Accepted, """{"index_id":"idx_3f9a1c","status":"pending"}""");

        var created = await CreateClient(handler).CreateIndexAsync("https://github.com/pallets/flask", "main", "repo_record_42");

        Assert.Equal(new RagIndexCreated("idx_3f9a1c", IndexStatus.Pending), created);
        using var body = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.Equal("https://github.com/pallets/flask", body.RootElement.GetProperty("repo_url").GetString());
        Assert.Equal("main", body.RootElement.GetProperty("ref").GetString());
        Assert.Equal("repo_record_42", body.RootElement.GetProperty("client_ref").GetString());
    }

    [Fact]
    public async Task CreateIndex_Without_Ref_Does_Not_Send_Ref_So_RAG_Uses_The_Default_Branch()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.Accepted, """{"index_id":"idx_3f9a1c","status":"pending"}""");

        await CreateClient(handler).CreateIndexAsync("https://github.com/pallets/flask", null, "repo_record_42");

        using var body = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.False(body.RootElement.TryGetProperty("ref", out _), "`ref` must be omitted, not sent as null or a guessed branch");
        Assert.Equal("repo_record_42", body.RootElement.GetProperty("client_ref").GetString());
    }

    [Fact]
    public async Task Query_Example_Request_Matches_The_Contract_Body()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.OK, QueryExample);

        await CreateClient(handler).QueryAsync("idx_3f9a1c", "JWT duoc kiem tra o dau?", 5);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body!);
        var root = body.RootElement;
        Assert.Equal("idx_3f9a1c", root.GetProperty("index_id").GetString());
        Assert.Equal("JWT duoc kiem tra o dau?", root.GetProperty("question").GetString());
        Assert.Equal(5, root.GetProperty("top_k").GetInt32());
        Assert.Equal("dense", root.GetProperty("options").GetProperty("mode").GetString());
        Assert.False(root.GetProperty("options").GetProperty("rerank").GetBoolean());
        Assert.False(root.GetProperty("debug").GetBoolean());
    }

    // ---------- responses: yaml examples ----------

    [Fact]
    public async Task Health_Example()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.OK, """{"status":"ok"}""");

        Assert.True(await CreateClient(handler).IsHealthyAsync());
    }

    [Fact]
    public async Task GetIndex_Ready_Example()
    {
        var json = """
        {"index_id":"idx_3f9a1c","status":"ready","stage":null,
         "progress":{"files_done":24,"total_files":24},
         "commit_sha":"d086db856be187255b8ec61ef409357393020f32",
         "stats":{"files":24,"chunks":412},"error":null}
        """;

        var s = await CreateClient(new StubHandler().Respond(HttpStatusCode.OK, json)).GetIndexAsync("idx_3f9a1c");

        Assert.Equal(new RagIndexStatus("idx_3f9a1c", IndexStatus.Ready, null, 24, 24,
            "d086db856be187255b8ec61ef409357393020f32", 24, 412, null), s);
    }

    [Fact]
    public async Task GetIndex_Embedding_Example_Has_Null_Stats()
    {
        var json = """
        {"index_id":"idx_3f9a1c","status":"embedding","stage":"embedding",
         "progress":{"files_done":10,"total_files":24},
         "commit_sha":"d086db856be187255b8ec61ef409357393020f32","stats":null,"error":null}
        """;

        var s = await CreateClient(new StubHandler().Respond(HttpStatusCode.OK, json)).GetIndexAsync("idx_3f9a1c");

        Assert.Equal(IndexStatus.Embedding, s.Status);
        Assert.Equal("embedding", s.Stage);
        Assert.Equal((10, 24), (s.FilesDone, s.TotalFiles));
        Assert.Null(s.Files);
        Assert.Null(s.Chunks);
        Assert.Null(s.Error);
    }

    [Fact]
    public async Task GetIndex_Failed_Example_Has_Null_Progress_And_CommitSha_And_Error_Without_Details()
    {
        var json = """
        {"index_id":"idx_3f9a1c","status":"failed","stage":"cloning","progress":null,"commit_sha":null,"stats":null,
         "error":{"code":"REPO_TOO_LARGE","message":"Repository exceeds the configured size limit"}}
        """;

        var s = await CreateClient(new StubHandler().Respond(HttpStatusCode.OK, json)).GetIndexAsync("idx_3f9a1c");

        Assert.Equal(IndexStatus.Failed, s.Status);
        Assert.Equal("cloning", s.Stage);
        Assert.Equal((0, 0), (s.FilesDone, s.TotalFiles)); // progress null -> no counters yet
        Assert.Null(s.CommitSha);
        Assert.Equal(new RagIndexError("REPO_TOO_LARGE", "Repository exceeds the configured size limit"), s.Error);
    }

    [Fact]
    public async Task GetIndex_Pending_Has_Null_Stage_And_Progress()
    {
        var json = """{"index_id":"idx_3f9a1c","status":"pending","stage":null,"progress":null,"commit_sha":null,"stats":null,"error":null}""";

        var s = await CreateClient(new StubHandler().Respond(HttpStatusCode.OK, json)).GetIndexAsync("idx_3f9a1c");

        Assert.Equal(IndexStatus.Pending, s.Status);
        Assert.Null(s.Stage);
        Assert.Equal((0, 0), (s.FilesDone, s.TotalFiles));
    }

    private const string QueryExample = """
        {"answer":"JWT duoc kiem tra trong `verify_token` [1].",
         "citations":[{"ref_id":1,"file_path":"app/core/security.py","symbol":"verify_token","symbol_kind":"function",
                       "start_line":34,"end_line":58,"snippet":"def verify_token(token: str) -> dict:\n    ...","score":0.91}],
         "usage":null,"latency_ms":850,"debug":null}
        """;

    [Fact]
    public async Task Query_Example_With_Null_Usage_And_Debug()
    {
        var answer = await CreateClient(new StubHandler().Respond(HttpStatusCode.OK, QueryExample)).QueryAsync("idx_3f9a1c", "q", 5);

        Assert.Equal("JWT duoc kiem tra trong `verify_token` [1].", answer.Answer);
        Assert.Equal(new RagCitation(1, "app/core/security.py", "verify_token", "function", 34, 58,
            "def verify_token(token: str) -> dict:\n    ...", 0.91), Assert.Single(answer.Citations));
        Assert.Null(answer.Usage);
        Assert.Equal(850, answer.LatencyMs);
    }

    [Fact]
    public async Task Query_Citation_With_Null_Symbol_And_SymbolKind()
    {
        var json = """
        {"answer":"See [1].","citations":[{"ref_id":1,"file_path":"README.md","symbol":null,"symbol_kind":null,
                                           "start_line":1,"end_line":9,"snippet":"# Title","score":0.5}],
         "usage":null,"latency_ms":10,"debug":null}
        """;

        var answer = await CreateClient(new StubHandler().Respond(HttpStatusCode.OK, json)).QueryAsync("idx_1", "q", 5);

        var c = Assert.Single(answer.Citations);
        Assert.Null(c.Symbol);
        Assert.Null(c.SymbolKind);
        Assert.Equal("README.md", c.FilePath);
    }

    [Fact]
    public async Task Query_Usage_Object_With_Null_Fields_Is_Accepted()
    {
        var json = """{"answer":"A [1].","citations":[],"usage":{"input_tokens":null,"output_tokens":null},"latency_ms":0}""";

        var answer = await CreateClient(new StubHandler().Respond(HttpStatusCode.OK, json)).QueryAsync("idx_1", "q", 5);

        Assert.NotNull(answer.Usage);
        Assert.Equal(JsonValueKind.Null, answer.Usage!.Value.GetProperty("input_tokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, answer.Usage!.Value.GetProperty("output_tokens").ValueKind);
    }

    [Fact]
    public async Task Query_Usage_Object_With_Token_Counts_Is_Kept()
    {
        var json = """{"answer":"A [1].","citations":[],"usage":{"input_tokens":120,"output_tokens":45},"latency_ms":900}""";

        var answer = await CreateClient(new StubHandler().Respond(HttpStatusCode.OK, json)).QueryAsync("idx_1", "q", 5);

        Assert.Equal(120, answer.Usage!.Value.GetProperty("input_tokens").GetInt32());
        Assert.Equal(45, answer.Usage!.Value.GetProperty("output_tokens").GetInt32());
    }

    [Fact]
    public async Task GetFile_Example()
    {
        var json = """
        {"path":"src/flask/app.py","start":34,"end":36,
         "content":"def verify_token(token: str) -> dict:\n    ...\n    return payload\n","language":"python"}
        """;
        var handler = new StubHandler().Respond(HttpStatusCode.OK, json);

        var file = await CreateClient(handler).GetFileAsync("idx_3f9a1c", "src/flask/app.py", 34, 36);

        Assert.Equal(new RagFile("src/flask/app.py", 34, 36, "def verify_token(token: str) -> dict:\n    ...\n    return payload\n", "python"), file);
        Assert.Equal("/v1/indexes/idx_3f9a1c/files", handler.Requests.Single().Uri.AbsolutePath);
    }

    [Fact]
    public async Task GetFile_With_Null_Language()
    {
        var json = """{"path":"LICENSE","start":1,"end":2,"content":"MIT\n","language":null}""";

        var file = await CreateClient(new StubHandler().Respond(HttpStatusCode.OK, json)).GetFileAsync("idx_1", "LICENSE", 1, 2);

        Assert.Null(file.Language);
        Assert.Equal("MIT\n", file.Content);
    }

    [Fact]
    public async Task Delete_Example_204()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.NoContent);

        await CreateClient(handler).DeleteIndexAsync("idx_3f9a1c");

        Assert.Equal(HttpMethod.Delete, handler.Requests.Single().Method);
    }

    // ---------- responses: yaml error envelopes (the exact examples) ----------

    public static TheoryData<int, string, string, RagFailureKind> ErrorExamples => new()
    {
        { 401, """{"error":{"code":"UNAUTHORIZED","message":"Missing or invalid API key","details":{}}}""", "UNAUTHORIZED", RagFailureKind.Unauthorized },
        { 404, """{"error":{"code":"INDEX_NOT_FOUND","message":"Index not found","details":{"index_id":"idx_3f9a1c"}}}""", "INDEX_NOT_FOUND", RagFailureKind.NotFound },
        { 404, """{"error":{"code":"FILE_NOT_FOUND","message":"File not found in index snapshot","details":{"path":"src/flask/missing.py"}}}""", "FILE_NOT_FOUND", RagFailureKind.NotFound },
        { 409, """{"error":{"code":"INDEX_NOT_READY","message":"Index is not ready","details":{"status":"embedding"}}}""", "INDEX_NOT_READY", RagFailureKind.NotReady },
        { 422, """{"error":{"code":"INVALID_REPO","message":"Only public GitHub repositories (https://github.com/<owner>/<repo>) are supported","details":{"repo_url":"https://gitlab.com/org/repo"}}}""", "INVALID_REPO", RagFailureKind.Invalid },
        { 422, """{"error":{"code":"INVALID_REQUEST","message":"Validation failed","details":{"field":"question","reason":"exceeds maximum length of 2000 characters"}}}""", "INVALID_REQUEST", RagFailureKind.Invalid },
        { 400, """{"error":{"code":"INVALID_RANGE","message":"Requested line range is invalid","details":{"start":500,"end":100}}}""", "INVALID_RANGE", RagFailureKind.Invalid },
        { 502, """{"error":{"code":"LLM_ERROR","message":"The language model request failed","details":{}}}""", "LLM_ERROR", RagFailureKind.LlmError },
        { 504, """{"error":{"code":"TIMEOUT","message":"The request timed out","details":{}}}""", "TIMEOUT", RagFailureKind.Timeout },
        { 500, """{"error":{"code":"INTERNAL_ERROR","message":"Unexpected server error","details":{}}}""", "INTERNAL_ERROR", RagFailureKind.Internal },
    };

    [Theory]
    [MemberData(nameof(ErrorExamples))]
    public async Task Error_Envelope_Examples_Are_Parsed_By_Code(int status, string json, string code, RagFailureKind kind)
    {
        var handler = new StubHandler().Respond((HttpStatusCode)status, json);

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryAsync("idx_3f9a1c", "q", 5));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal(code, ex.Code);
        Assert.Equal(status, ex.UpstreamStatus);
    }

    [Fact]
    public async Task CreateIndex_422_INVALID_REPO_Example_Is_Invalid()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.UnprocessableEntity, (string)ErrorExamples.First(e => (string)e[2] == "INVALID_REPO")[1]);

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).CreateIndexAsync("https://gitlab.com/org/repo", null, "c"));

        Assert.Equal(RagFailureKind.Invalid, ex.Kind);
        Assert.Equal("INVALID_REPO", ex.Code);
    }

    // ---------- errors that do NOT follow ErrorResponse: fall back to the status ----------

    [Fact]
    public async Task Prism_422_Problem_Json_Falls_Back_To_The_Status()
    {
        // Prism answers a malformed request with application/problem+json instead of the contract's ErrorResponse.
        var json = """{"type":"https://stoplight.io/prism/errors#UNPROCESSABLE_ENTITY","title":"Invalid request","status":422,"detail":"Your request body is not valid"}""";
        var handler = new StubHandler().Respond(HttpStatusCode.UnprocessableEntity, json);

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryAsync("idx_1", "q", 5));

        Assert.Equal(RagFailureKind.Invalid, ex.Kind);
        Assert.Equal(422, ex.UpstreamStatus);
    }

    [Theory]
    [InlineData(502, RagFailureKind.LlmError)]
    [InlineData(503, RagFailureKind.Unavailable)]
    [InlineData(504, RagFailureKind.Timeout)]
    public async Task Html_Error_Page_From_A_Proxy_Falls_Back_To_The_Status(int status, RagFailureKind kind)
    {
        var handler = new StubHandler().Respond((HttpStatusCode)status, "<html><body>Bad gateway</body></html>");

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryAsync("idx_1", "q", 5));

        Assert.Equal(kind, ex.Kind);
    }

    [Fact]
    public async Task Error_Envelope_With_An_Unknown_Code_Falls_Back_To_The_Status_But_Keeps_The_Code()
    {
        var json = """{"error":{"code":"SOMETHING_NEW","message":"x","details":{}}}""";
        var handler = new StubHandler().Respond(HttpStatusCode.Conflict, json);

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryAsync("idx_1", "q", 5));

        Assert.Equal(RagFailureKind.NotReady, ex.Kind);
        Assert.Equal("SOMETHING_NEW", ex.Code);
    }

    // ---------- INVALID_REQUEST as the frontend sees it ----------

    [Fact]
    public void Invalid_Request_Maps_To_A_Generic_422_Not_The_Repository_Message()
    {
        var problem = RagProblemMapper.ToProblem(new RagException(RagFailureKind.Invalid, RagErrorCodes.InvalidRequest, 422, "Validation failed"));

        Assert.Equal(422, problem.Status);
        Assert.Equal("Yêu cầu không hợp lệ", problem.Title);
        Assert.Equal("INVALID_REQUEST", problem.Extensions["code"]);
        Assert.DoesNotContain("Repository", problem.Title + problem.Detail);
        Assert.DoesNotContain("Validation failed", problem.Detail); // upstream text is never exposed
    }

    [Fact]
    public void Invalid_Repo_Still_Uses_The_Repository_Message()
    {
        var problem = RagProblemMapper.ToProblem(new RagException(RagFailureKind.Invalid, RagErrorCodes.InvalidRepo, 422));

        Assert.Equal("Repository không hợp lệ", problem.Title);
        Assert.Equal("INVALID_REPO", problem.Extensions["code"]);
    }
}
