using System.Net;
using System.Text.Json;
using Application.Rag;
using Domain.Enums;
using Infrastructure.Options;
using Infrastructure.Rag;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Tests.Rag;

public class HttpRagClientTests
{
    private const string ApiKey = "super-secret-rag-key";
    private const string Question = "where is the SECRET-QUESTION-TEXT handled?";

    private readonly CapturingLoggerProvider _logs = new();

    private HttpRagClient CreateClient(StubHandler handler, Action<RagOptions>? configure = null)
    {
        var options = new RagOptions
        {
            BaseUrl = "http://rag.test:4010",
            ApiKey = ApiKey,
            StatusRetryDelay = TimeSpan.Zero,
        };
        configure?.Invoke(options);

        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://rag.test:4010/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs))
            .CreateLogger<HttpRagClient>();
        return new HttpRagClient(http, Options.Create(options), logger);
    }

    private static string ErrorJson(string code, string message = "boom") =>
        JsonSerializer.Serialize(new { error = new { code, message, details = (object?)null } });

    // ---------- happy paths: all 6 endpoints ----------

    [Fact]
    public async Task Health_Returns_True_On_200_And_Sends_No_Api_Key()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.OK, "{\"status\":\"ok\"}");

        var healthy = await CreateClient(handler).IsHealthyAsync();

        Assert.True(healthy);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("/v1/health", req.Uri.AbsolutePath);
        Assert.False(req.Headers.ContainsKey("X-API-Key"));
    }

    [Fact]
    public async Task Health_Returns_False_When_Unreachable()
    {
        var handler = new StubHandler().Throw(new HttpRequestException("connection refused"));

        Assert.False(await CreateClient(handler).IsHealthyAsync());
    }

    [Fact]
    public async Task CreateIndex_Posts_SnakeCase_Body_With_Headers_And_Parses_202()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.Accepted, "{\"index_id\":\"idx-1\",\"status\":\"pending\"}");

        var created = await CreateClient(handler).CreateIndexAsync("https://github.com/o/r", "main", "client-ref-1");

        Assert.Equal(new RagIndexCreated("idx-1", IndexStatus.Pending), created);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/v1/indexes", req.Uri.AbsolutePath);
        Assert.Equal(ApiKey, req.Headers["X-API-Key"]);
        Assert.False(string.IsNullOrWhiteSpace(req.Headers["X-Request-ID"]));
        using var body = JsonDocument.Parse(req.Body!);
        Assert.Equal("https://github.com/o/r", body.RootElement.GetProperty("repo_url").GetString());
        Assert.Equal("main", body.RootElement.GetProperty("ref").GetString());
        Assert.Equal("client-ref-1", body.RootElement.GetProperty("client_ref").GetString());
    }

    [Fact]
    public async Task GetIndex_Parses_Full_Status()
    {
        var json = """
        {"index_id":"idx-1","status":"embedding","stage":"embedding",
         "progress":{"files_done":3,"total_files":10},"commit_sha":"abc123",
         "stats":{"files":10,"chunks":42},"error":null}
        """;
        var handler = new StubHandler().Respond(HttpStatusCode.OK, json);

        var status = await CreateClient(handler).GetIndexAsync("idx-1");

        Assert.Equal(IndexStatus.Embedding, status.Status);
        Assert.Equal("embedding", status.Stage);
        Assert.Equal(3, status.FilesDone);
        Assert.Equal(10, status.TotalFiles);
        Assert.Equal("abc123", status.CommitSha);
        Assert.Equal(42, status.Chunks);
        Assert.Null(status.Error);
        Assert.Equal("/v1/indexes/idx-1", handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task GetIndex_Failed_Status_Exposes_Error_Code()
    {
        var json = """{"index_id":"i","status":"failed","error":{"code":"REPO_TOO_LARGE","message":"too big"}}""";
        var handler = new StubHandler().Respond(HttpStatusCode.OK, json);

        var status = await CreateClient(handler).GetIndexAsync("i");

        Assert.Equal(IndexStatus.Failed, status.Status);
        Assert.Equal("REPO_TOO_LARGE", status.Error!.Code);
    }

    [Fact]
    public async Task Query_Sends_Contract_Body_And_Parses_Answer_With_Citations()
    {
        var json = """
        {"answer":"It lives in Foo [1].",
         "citations":[{"ref_id":1,"file_path":"src/Foo.cs","symbol":"Foo","symbol_kind":"class",
                       "start_line":10,"end_line":20,"snippet":"class Foo {}","score":0.87}],
         "usage":{"prompt_tokens":10,"completion_tokens":5},"latency_ms":812}
        """;
        var handler = new StubHandler().Respond(HttpStatusCode.OK, json);

        var answer = await CreateClient(handler).QueryAsync("idx-1", Question, 8);

        Assert.Equal("It lives in Foo [1].", answer.Answer);
        var c = Assert.Single(answer.Citations);
        Assert.Equal(new RagCitation(1, "src/Foo.cs", "Foo", "class", 10, 20, "class Foo {}", 0.87), c);
        Assert.Equal(812, answer.LatencyMs);
        Assert.Equal(10, answer.Usage!.Value.GetProperty("prompt_tokens").GetInt32());

        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        var root = body.RootElement;
        Assert.Equal("idx-1", root.GetProperty("index_id").GetString());
        Assert.Equal(Question, root.GetProperty("question").GetString());
        Assert.Equal(8, root.GetProperty("top_k").GetInt32());
        Assert.Equal("dense", root.GetProperty("options").GetProperty("mode").GetString());
        Assert.False(root.GetProperty("options").GetProperty("rerank").GetBoolean());
        Assert.False(root.GetProperty("debug").GetBoolean());
    }

    [Fact]
    public async Task GetFile_Escapes_Path_And_Parses_Response()
    {
        var json = """{"path":"src/a b.cs","start":1,"end":3,"content":"line1\nline2\nline3","language":"csharp"}""";
        var handler = new StubHandler().Respond(HttpStatusCode.OK, json);

        var file = await CreateClient(handler).GetFileAsync("idx-1", "src/a b.cs", 1, 3);

        Assert.Equal("csharp", file.Language);
        Assert.Equal("line1\nline2\nline3", file.Content);
        var uri = handler.Requests[0].Uri;
        Assert.Equal("/v1/indexes/idx-1/files", uri.AbsolutePath);
        Assert.Contains("path=src%2Fa%20b.cs", uri.Query);
        Assert.Contains("start=1", uri.Query);
        Assert.Contains("end=3", uri.Query);
    }

    [Fact]
    public async Task Delete_Succeeds_On_204_And_Is_Idempotent_On_404()
    {
        var ok = new StubHandler().Respond(HttpStatusCode.NoContent);
        await CreateClient(ok).DeleteIndexAsync("idx-1");
        Assert.Equal(HttpMethod.Delete, ok.Requests[0].Method);

        var gone = new StubHandler().Respond(HttpStatusCode.NotFound, ErrorJson("INDEX_NOT_FOUND"));
        await CreateClient(gone).DeleteIndexAsync("idx-1"); // must not throw
    }

    // ---------- error mapping: one test per contract error ----------

    [Theory]
    [InlineData(401, "UNAUTHORIZED", RagFailureKind.Unauthorized)]
    [InlineData(404, "INDEX_NOT_FOUND", RagFailureKind.NotFound)]
    [InlineData(404, "REPO_NOT_FOUND", RagFailureKind.NotFound)]
    [InlineData(404, "FILE_NOT_FOUND", RagFailureKind.NotFound)]
    [InlineData(409, "INDEX_NOT_READY", RagFailureKind.NotReady)]
    [InlineData(422, "INVALID_REPO", RagFailureKind.Invalid)]
    [InlineData(422, "REPO_TOO_LARGE", RagFailureKind.Invalid)]
    [InlineData(400, "INVALID_RANGE", RagFailureKind.Invalid)]
    [InlineData(502, "LLM_ERROR", RagFailureKind.LlmError)]
    [InlineData(504, "TIMEOUT", RagFailureKind.Timeout)]
    [InlineData(500, "INTERNAL_ERROR", RagFailureKind.Internal)]
    public async Task Error_Responses_Are_Classified_By_Code(int status, string code, RagFailureKind expected)
    {
        var handler = new StubHandler().Respond((HttpStatusCode)status, ErrorJson(code));

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryAsync("i", "q", 5));

        Assert.Equal(expected, ex.Kind);
        Assert.Equal(code, ex.Code);
        Assert.Equal(status, ex.UpstreamStatus);
    }

    [Theory]
    [InlineData(401, RagFailureKind.Unauthorized)]
    [InlineData(404, RagFailureKind.NotFound)]
    [InlineData(409, RagFailureKind.NotReady)]
    [InlineData(422, RagFailureKind.Invalid)]
    [InlineData(502, RagFailureKind.LlmError)]
    [InlineData(503, RagFailureKind.Unavailable)]
    [InlineData(504, RagFailureKind.Timeout)]
    [InlineData(500, RagFailureKind.Internal)]
    public async Task Non_Contract_Error_Bodies_Fall_Back_To_The_Status_Code(int status, RagFailureKind expected)
    {
        var handler = new StubHandler().Respond((HttpStatusCode)status, "<html>Bad gateway</html>");

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryAsync("i", "q", 5));

        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public async Task Connection_Failure_Is_Unavailable()
    {
        var handler = new StubHandler().Throw(new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryAsync("i", "q", 5));

        Assert.Equal(RagFailureKind.Unavailable, ex.Kind);
        Assert.Equal(RagErrorCodes.Unavailable, ex.Code);
    }

    [Fact]
    public async Task Client_Side_Timeout_Is_Reported_As_TIMEOUT()
    {
        var handler = new StubHandler().Hang();
        var client = CreateClient(handler, o => o.QueryTimeout = TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAsync<RagException>(() => client.QueryAsync("i", "q", 5));

        Assert.Equal(RagFailureKind.Timeout, ex.Kind);
        Assert.Equal(RagErrorCodes.Timeout, ex.Code);
    }

    [Fact]
    public async Task Caller_Cancellation_Is_Not_Reported_As_A_RAG_Timeout()
    {
        var handler = new StubHandler().Hang();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(handler).QueryAsync("i", "q", 5, ct: cts.Token));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    public async Task Malformed_Success_Bodies_Are_InvalidResponse(string body)
    {
        var handler = new StubHandler().Respond(HttpStatusCode.OK, body);

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryAsync("i", "q", 5));

        Assert.Equal(RagFailureKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public async Task Unknown_Index_Status_Is_InvalidResponse()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.OK, """{"index_id":"i","status":"exploded"}""");

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).GetIndexAsync("i"));

        Assert.Equal(RagFailureKind.InvalidResponse, ex.Kind);
    }

    // ---------- timeout / retry policy (plan section 0) ----------

    [Fact]
    public async Task Query_Is_Never_Retried()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.ServiceUnavailable, ErrorJson("INTERNAL_ERROR"));

        await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).QueryAsync("i", "q", 5));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CreateIndex_Is_Never_Retried()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).CreateIndexAsync("https://github.com/o/r", "main", "c"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetIndex_Retries_Transient_Failures_Up_To_Twice_Then_Succeeds()
    {
        var handler = new StubHandler()
            .Throw(new HttpRequestException("reset"))
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.OK, """{"index_id":"i","status":"ready"}""");

        var status = await CreateClient(handler).GetIndexAsync("i");

        Assert.Equal(IndexStatus.Ready, status.Status);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task GetIndex_Gives_Up_After_Two_Retries()
    {
        var handler = new StubHandler().Respond(HttpStatusCode.ServiceUnavailable);

        var ex = await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).GetIndexAsync("i"));

        Assert.Equal(RagFailureKind.Unavailable, ex.Kind);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(404)]
    public async Task GetIndex_Does_Not_Retry_Permanent_Errors(int status)
    {
        var handler = new StubHandler().Respond((HttpStatusCode)status, ErrorJson("INDEX_NOT_FOUND"));

        await Assert.ThrowsAsync<RagException>(() => CreateClient(handler).GetIndexAsync("i"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetIndex_Times_Out_Per_Request_And_Retries()
    {
        var handler = new StubHandler().Hang();
        var client = CreateClient(handler, o => o.StatusTimeout = TimeSpan.FromMilliseconds(50));

        var ex = await Assert.ThrowsAsync<RagException>(() => client.GetIndexAsync("i"));

        Assert.Equal(RagFailureKind.Timeout, ex.Kind);
        Assert.Equal(3, handler.Requests.Count);
    }

    // ---------- secrets / question never logged ----------

    [Fact]
    public async Task Question_ApiKey_And_Bodies_Are_Never_Logged()
    {
        var ok = new StubHandler().Respond(HttpStatusCode.OK, """{"answer":"ANSWER-BODY-TEXT","citations":[]}""");
        await CreateClient(ok).QueryAsync("idx", Question, 5);

        var failing = new StubHandler().Respond(HttpStatusCode.BadGateway, ErrorJson("LLM_ERROR", $"failed on {Question}"));
        await Assert.ThrowsAsync<RagException>(() => CreateClient(failing).QueryAsync("idx", Question, 5));

        var unreachable = new StubHandler().Throw(new HttpRequestException($"cannot reach with {ApiKey}"));
        await Assert.ThrowsAsync<RagException>(() => CreateClient(unreachable).QueryAsync("idx", Question, 5));

        var logs = string.Join("\n", _logs.Entries);
        Assert.NotEmpty(logs); // something was logged, so the assertions below mean something
        Assert.DoesNotContain("SECRET-QUESTION-TEXT", logs);
        Assert.DoesNotContain(ApiKey, logs);
        Assert.DoesNotContain("ANSWER-BODY-TEXT", logs);
    }
}
