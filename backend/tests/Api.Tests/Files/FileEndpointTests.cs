using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Projects;
using Api.Tests.Rag;
using Application.Projects;
using Application.Rag;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Files;

[Collection(ApiCollection.Name)]
public class FileEndpointTests(ApiFactory factory)
{
    private const string RagKey = "super-secret-rag-key";
    private const string IndexId = "idx_secret123";

    private record Problem(int Status, string? Title, string? Detail, string? Code, Dictionary<string, string[]>? Errors);

    private WebApplicationFactory<Program> WithRag(IRagClient rag) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(rag)));

    private sealed record Seeded(AuthTestClient Client, Guid ProjectId, Guid RepositoryId);

    /// <summary>A logged-in user with a project and one repository row inserted directly in the given state (no RAG calls).</summary>
    private static async Task<Seeded> SeedAsync(WebApplicationFactory<Program> app, IndexStatus status = IndexStatus.Ready,
        string? indexId = IndexId, string? commitSha = "abc123")
    {
        var client = await AuthTestClient.NewUserAsync(app);
        var project = (await (await client.PostAsync("/api/projects", new { name = "p" }))
            .Content.ReadFromJsonAsync<ProjectDetailDto>())!;

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.Repositories.Add(new CodeRepository
        {
            Id = id, ProjectId = project.Id, RepoUrl = "https://github.com/octo/hello", Status = status,
            IndexId = indexId, CommitSha = commitSha,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return new Seeded(client, project.Id, id);
    }

    private static Task<HttpResponseMessage> GetFileAsync(Seeded s, string query) =>
        s.Client.GetAsync($"/api/repositories/{s.RepositoryId}/files{query}");

    private static async Task<Problem> ProblemOf(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<Problem>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    // ---------- authentication and ownership ----------

    [Fact]
    public async Task Requires_Authentication()
    {
        var response = await factory.CreateAuthClient()
            .GetAsync("/api/repositories/00000000-0000-0000-0000-000000000001/files?path=a.cs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Another_Users_Repository_Looks_Exactly_Like_One_That_Does_Not_Exist()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var owner = await SeedAsync(app);
        var intruder = await AuthTestClient.NewUserAsync(app);

        var foreign = await intruder.GetAsync($"/api/repositories/{owner.RepositoryId}/files?path=src/a.cs");
        var missing = await intruder.GetAsync($"/api/repositories/{Guid.NewGuid()}/files?path=src/a.cs");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var (a, b) = (await ProblemOf(foreign), await ProblemOf(missing));
        Assert.Equal((a.Status, a.Title, a.Detail, a.Code), (b.Status, b.Title, b.Detail, b.Code));
        Assert.Empty(rag.FileCalls);

        // The owner still reads it fine.
        Assert.Equal(HttpStatusCode.OK, (await GetFileAsync(owner, "?path=src/a.cs")).StatusCode);
    }

    [Fact]
    public async Task Bad_Input_On_Someone_Elses_Repository_Is_Still_404_Not_A_Validation_Error()
    {
        using var app = WithRag(new RecordingRagClient());
        var owner = await SeedAsync(app);
        var intruder = await AuthTestClient.NewUserAsync(app);

        var response = await intruder.GetAsync($"/api/repositories/{owner.RepositoryId}/files?path=../etc/passwd&start=0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- success ----------

    [Fact]
    public async Task Returns_The_File_Slice_As_CamelCase_With_The_Repository_CommitSha()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SeedAsync(app);

        var response = await GetFileAsync(s, "?path=src/Foo.cs&start=10&end=12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(["commitSha", "content", "end", "language", "path", "start"],
            root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("src/Foo.cs", root.GetProperty("path").GetString());
        Assert.Equal(10, root.GetProperty("start").GetInt32());
        Assert.Equal(12, root.GetProperty("end").GetInt32());
        Assert.Equal("line one\nline two\nline three\n", root.GetProperty("content").GetString());
        Assert.Equal("csharp", root.GetProperty("language").GetString());
        Assert.Equal("abc123", root.GetProperty("commitSha").GetString());
        Assert.Equal(("src/Foo.cs", 10, 12), (rag.FileCalls.Single().Path, rag.FileCalls.Single().Start, rag.FileCalls.Single().End));
        Assert.Equal(IndexId, rag.FileCalls.Single().IndexId);
    }

    [Fact]
    public async Task Start_Defaults_To_1_And_End_To_Start_Plus_1999()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SeedAsync(app);

        await GetFileAsync(s, "?path=a.cs");
        await GetFileAsync(s, "?path=a.cs&start=500");
        await GetFileAsync(s, "?path=a.cs&start=2147483647"); // the default window must not overflow

        Assert.Equal([(1, 2000), (500, 2499), (int.MaxValue, int.MaxValue)], rag.FileCalls.Select(c => (c.Start, c.End)));
    }

    [Fact]
    public async Task Exactly_2000_Lines_Is_Allowed()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SeedAsync(app);

        var response = await GetFileAsync(s, "?path=a.cs&start=1&end=2000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((1, 2000), (rag.FileCalls.Single().Start, rag.FileCalls.Single().End));
    }

    [Fact]
    public async Task Content_Is_Returned_Verbatim_And_Null_Language_And_CommitSha_Are_Allowed()
    {
        const string html = "<script>alert('x')</script>\n# not markdown";
        var rag = new RecordingRagClient { OnGetFile = c => new RagFile(c.Path, c.Start, c.End, html, null) };
        using var app = WithRag(rag);
        var s = await SeedAsync(app, commitSha: null);

        var response = await GetFileAsync(s, "?path=LICENSE&start=1&end=2");

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(html, body.GetProperty("content").GetString()); // no sanitising on the backend: the frontend escapes
        Assert.Equal(JsonValueKind.Null, body.GetProperty("language").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("commitSha").ValueKind);
    }

    [Fact]
    public async Task The_End_RAG_Reports_After_Clipping_Is_What_The_Frontend_Gets()
    {
        var rag = new RecordingRagClient { OnGetFile = c => new RagFile(c.Path, c.Start, 7, "x", "python") };
        using var app = WithRag(rag);
        var s = await SeedAsync(app);

        var body = await (await GetFileAsync(s, "?path=a.py&start=5&end=100")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(7, body.GetProperty("end").GetInt32());
    }

    // ---------- repository state: RAG is not called ----------

    [Theory]
    [InlineData(IndexStatus.Pending)]
    [InlineData(IndexStatus.Cloning)]
    [InlineData(IndexStatus.Parsing)]
    [InlineData(IndexStatus.Embedding)]
    public async Task A_Repository_That_Is_Not_Ready_Is_409_INDEX_NOT_READY_And_RAG_Is_Not_Called(IndexStatus status)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SeedAsync(app, status);

        var response = await GetFileAsync(s, "?path=a.cs");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("INDEX_NOT_READY", (await ProblemOf(response)).Code);
        Assert.Empty(rag.FileCalls);
        Assert.Equal(0, rag.GetCalls); // not even a status refresh
    }

    [Theory]
    [InlineData(IndexStatus.Pending)]
    [InlineData(IndexStatus.Ready)]
    public async Task A_Repository_Without_An_Index_Id_Is_409_NO_REPOSITORY(IndexStatus status)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SeedAsync(app, status, indexId: null);

        var response = await GetFileAsync(s, "?path=a.cs");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NO_REPOSITORY", (await ProblemOf(response)).Code);
        Assert.Empty(rag.FileCalls);
    }

    [Fact]
    public async Task A_Failed_Repository_Is_409_REPOSITORY_FAILED()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SeedAsync(app, IndexStatus.Failed);

        var response = await GetFileAsync(s, "?path=a.cs");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("REPOSITORY_FAILED", (await ProblemOf(response)).Code);
        Assert.Empty(rag.FileCalls);
    }

    // ---------- validation: nothing invalid reaches RAG ----------

    [Theory]
    [InlineData("")]                         // path missing
    [InlineData("?path=")]
    [InlineData("?path=%20%20")]             // blank
    [InlineData("?path=../secret.txt")]
    [InlineData("?path=src/../../etc/passwd")]
    [InlineData("?path=src/%2e%2e/x")]       // encoded ".."
    [InlineData("?path=..")]
    [InlineData("?path=/etc/passwd")]        // absolute
    [InlineData("?path=%5Cwindows%5Csystem32")] // leading backslash
    [InlineData("?path=src/a%00.cs")]        // NUL
    [InlineData("?path=src/a%0Ab.cs")]       // newline
    [InlineData("?path=src/a%09b.cs")]       // tab
    public async Task Invalid_Paths_Are_400_Validation_Errors_On_Path(string query)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SeedAsync(app);

        var response = await GetFileAsync(s, query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemOf(response);
        Assert.Contains("Path", problem.Errors!.Keys);
        Assert.Empty(rag.FileCalls);
    }

    [Fact]
    public async Task Path_Length_Is_Limited_To_1024_Characters()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SeedAsync(app);

        var ok = await GetFileAsync(s, "?path=" + new string('a', 1024));
        var tooLong = await GetFileAsync(s, "?path=" + new string('a', 1025));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("Path", (await ProblemOf(tooLong)).Errors!.Keys);
        Assert.Single(rag.FileCalls);
    }

    [Theory]
    [InlineData("&start=0")]
    [InlineData("&start=-5")]
    [InlineData("&end=0")]
    [InlineData("&end=-1")]
    [InlineData("&start=10&end=9")]
    [InlineData("&start=1&end=2001")]       // 2001 lines
    [InlineData("&start=100&end=2100")]     // 2001 lines
    [InlineData("&start=1&end=2147483647")]
    public async Task Invalid_Ranges_Are_400_INVALID_RANGE(string range)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SeedAsync(app);

        var response = await GetFileAsync(s, "?path=a.cs" + range);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemOf(response);
        Assert.Equal("INVALID_RANGE", problem.Code);
        Assert.Empty(rag.FileCalls);
    }

    [Theory]
    [InlineData("&start=abc")]
    [InlineData("&end=1.5")]
    [InlineData("&start=99999999999")]
    public async Task Non_Integer_Numbers_Are_400(string range)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SeedAsync(app);

        var response = await GetFileAsync(s, "?path=a.cs" + range);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(rag.FileCalls);
    }

    // ---------- RAG errors are mapped, not leaked ----------

    [Theory]
    [InlineData(RagFailureKind.NotFound, "FILE_NOT_FOUND", 404, "FILE_NOT_FOUND")]      // frontend falls back to the citation snapshot (D14)
    [InlineData(RagFailureKind.NotFound, "INDEX_NOT_FOUND", 404, "INDEX_NOT_FOUND")]
    [InlineData(RagFailureKind.Invalid, "INVALID_RANGE", 400, "INVALID_RANGE")]         // e.g. start past the end of the file
    [InlineData(RagFailureKind.NotReady, "INDEX_NOT_READY", 409, "INDEX_NOT_READY")]
    [InlineData(RagFailureKind.Timeout, "TIMEOUT", 504, "TIMEOUT")]
    [InlineData(RagFailureKind.Unavailable, "RAG_UNAVAILABLE", 502, "RAG_UNAVAILABLE")]
    [InlineData(RagFailureKind.Internal, "INTERNAL_ERROR", 502, "RAG_UNAVAILABLE")]
    [InlineData(RagFailureKind.InvalidResponse, "RAG_INVALID_RESPONSE", 502, "RAG_UNAVAILABLE")]
    [InlineData(RagFailureKind.Unauthorized, "UNAUTHORIZED", 502, "RAG_UNAVAILABLE")]   // our key was rejected: not the user's business
    public async Task RAG_Errors_Are_Mapped_To_Stable_Codes(RagFailureKind kind, string ragCode, int status, string code)
    {
        var rag = new RecordingRagClient { FileError = new RagException(kind, ragCode, 500, "internal upstream detail") };
        using var app = WithRag(rag);
        var s = await SeedAsync(app);

        var response = await GetFileAsync(s, "?path=src/Foo.cs");

        Assert.Equal(status, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(code, (await ProblemOf(response)).Code);
        Assert.DoesNotContain("internal upstream detail", text);
        Assert.DoesNotContain(IndexId, text);
    }

    // ---------- secrets, logs and request id through the real HTTP client ----------

    private WebApplicationFactory<Program> WithHttpRag(StubHandler stub) =>
        factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG_MODE"] = "http",
                ["RAG_BASE_URL"] = "http://rag.test:4010",
                ["RAG_API_KEY"] = RagKey,
            }));
            b.ConfigureTestServices(s => s.AddHttpClient<Infrastructure.Rag.HttpRagClient>()
                .ConfigurePrimaryHttpMessageHandler(() => stub));
        });

    [Fact]
    public async Task Request_Id_And_Api_Key_Go_To_RAG_And_Never_Come_Back_To_The_Frontend()
    {
        var stub = new StubHandler().Respond(HttpStatusCode.OK,
            """{"path":"src/flask/app.py","start":34,"end":36,"content":"def f():\n    pass\n","language":"python"}""");
        using var app = WithHttpRag(stub);
        var s = await SeedAsync(app);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/repositories/{s.RepositoryId}/files?path=src/flask/app.py&start=34&end=36");
        request.Headers.Add("X-Request-ID", "trace-abc-123");
        var response = await s.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(stub.Requests);
        Assert.Equal("/v1/indexes/idx_secret123/files", sent.Uri.AbsolutePath);
        Assert.Contains("path=src%2Fflask%2Fapp.py", sent.Uri.Query);
        Assert.Contains("start=34", sent.Uri.Query);
        Assert.Contains("end=36", sent.Uri.Query);
        Assert.Equal("trace-abc-123", sent.Headers["X-Request-ID"]);
        Assert.Equal(RagKey, sent.Headers["X-API-Key"]);

        Assert.Equal("trace-abc-123", response.Headers.GetValues("X-Request-ID").Single());
        var all = string.Join("\n", response.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}"))
                  + await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(RagKey, all);
        Assert.DoesNotContain("idx_secret123", all);
        Assert.DoesNotContain("indexId", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_RAG_FILE_NOT_FOUND_Envelope_Reaches_The_Frontend_As_404_FILE_NOT_FOUND()
    {
        var stub = new StubHandler().Respond(HttpStatusCode.NotFound,
            """{"error":{"code":"FILE_NOT_FOUND","message":"File not found in index snapshot","details":{"path":"src/flask/missing.py"}}}""");
        using var app = WithHttpRag(stub);
        var s = await SeedAsync(app);

        var response = await GetFileAsync(s, "?path=src/flask/missing.py");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal("FILE_NOT_FOUND", (await ProblemOf(response)).Code);
        Assert.DoesNotContain("idx_secret123", text);
        Assert.DoesNotContain(RagKey, text);
    }

    [Fact]
    public async Task File_Content_And_Path_Are_Never_Logged()
    {
        const string content = "SECRET-FILE-CONTENT-8841";
        const string path = "src/SECRET-PATH-2290.cs";
        var stub = new StubHandler().Respond(HttpStatusCode.OK,
            $$"""{"path":"{{path}}","start":1,"end":1,"content":"{{content}}","language":"csharp"}""");
        using var app = WithHttpRag(stub);
        var s = await SeedAsync(app);

        var ok = await GetFileAsync(s, $"?path={path}&start=1&end=1");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var rejected = await GetFileAsync(s, "?path=../SECRET-PATH-2290.cs");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var logs = string.Join("\n", factory.Logs.Entries);
        Assert.NotEmpty(logs);
        Assert.DoesNotContain("SECRET-FILE-CONTENT-8841", logs);
        Assert.DoesNotContain("SECRET-PATH-2290", logs);
        Assert.DoesNotContain(RagKey, logs);
    }
}
