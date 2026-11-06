using System.Net;
using System.Net.Http.Json;
using Api.Tests.Projects;
using Application.Chat;
using Application.Projects;
using Application.Rag;
using Application.Repositories;
using Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Chat;

[Collection(ApiCollection.Name)]
public class ChatEndpointTests(ApiFactory factory)
{
    private const string Question = "Where is the request handler wired up?";

    private record ProblemWithCode(int Status, string? Title, string? Detail, string? Code);

    private WebApplicationFactory<Program> WithRag(IRagClient rag) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(rag)));

    /// <summary>A logged-in user with a project whose repository is imported and (optionally) already ready.</summary>
    private sealed record Setup(AuthTestClient Client, Guid ProjectId, Guid RepositoryId, Guid ConversationId);

    private static async Task<Setup> SetupAsync(WebApplicationFactory<Program> app, RecordingRagClient rag, bool ready = true)
    {
        var client = await AuthTestClient.NewUserAsync(app);
        var project = (await (await client.PostAsync("/api/projects", new { name = "p" }))
            .Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var repo = (await (await client.PostAsync($"/api/projects/{project.Id}/repositories",
            new { repoUrl = "https://github.com/octo/hello" })).Content.ReadFromJsonAsync<RepositoryDto>())!;

        if (ready)
        {
            rag.OnGet = RecordingRagClient.Ready;
            await client.GetAsync($"/api/repositories/{repo.Id}"); // moves the stored status to ready
            rag.OnGet = null;
        }

        var conversation = await CreateConversationAsync(client, project.Id);
        return new Setup(client, project.Id, repo.Id, conversation.Id);
    }

    private static async Task<ConversationDto> CreateConversationAsync(AuthTestClient client, Guid projectId, string? title = null)
    {
        var response = await client.PostAsync($"/api/projects/{projectId}/conversations", new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConversationDto>())!;
    }

    private static Task<HttpResponseMessage> AskAsync(AuthTestClient client, Guid conversationId, string? question = Question, int? topK = null) =>
        client.PostAsync($"/api/conversations/{conversationId}/messages", new { question, topK });

    private static async Task<List<MessageDto>> HistoryAsync(AuthTestClient client, Guid conversationId) =>
        (await (await client.GetAsync($"/api/conversations/{conversationId}/messages")).Content
            .ReadFromJsonAsync<List<MessageDto>>())!;

    // ---------- authentication ----------

    [Theory]
    [InlineData("GET", "/api/projects/00000000-0000-0000-0000-000000000001/conversations")]
    [InlineData("POST", "/api/projects/00000000-0000-0000-0000-000000000001/conversations")]
    [InlineData("GET", "/api/conversations/00000000-0000-0000-0000-000000000001/messages")]
    [InlineData("POST", "/api/conversations/00000000-0000-0000-0000-000000000001/messages")]
    public async Task Endpoints_Require_Authentication(string method, string url)
    {
        var response = await factory.CreateAuthClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- conversations ----------

    [Fact]
    public async Task Conversations_Can_Be_Created_And_Listed_By_Latest_Activity()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);
        var other = await CreateConversationAsync(s.Client, s.ProjectId, "Explicit title");
        Assert.Equal("Explicit title", other.Title);

        await AskAsync(s.Client, s.ConversationId); // bumps the first conversation to the top

        var list = await (await s.Client.GetAsync($"/api/projects/{s.ProjectId}/conversations"))
            .Content.ReadFromJsonAsync<List<ConversationSummaryDto>>();
        Assert.Equal([s.ConversationId, other.Id], list!.Select(c => c.Id));
        Assert.Equal(2, list![0].MessageCount);
        Assert.Equal(0, list[1].MessageCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_Conversation_Rejects_Blank_Title(string title)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var response = await s.Client.PostAsync($"/api/projects/{s.ProjectId}/conversations", new { title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_Conversation_Rejects_Overlong_Title()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var response = await s.Client.PostAsync($"/api/projects/{s.ProjectId}/conversations",
            new { title = new string('t', ChatRules.MaxTitleLength + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- asking ----------

    [Fact]
    public async Task Ask_Calls_RAG_Once_With_The_Active_Index_And_Baseline_Options()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var response = await AskAsync(s.Client, s.ConversationId, $"   {Question}   ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var query = Assert.Single(rag.Queries);
        Assert.Equal(Question, query.Question); // trimmed
        Assert.Equal(ChatRules.DefaultTopK, query.TopK);
        Assert.Equal("dense", query.Options.Mode);
        Assert.False(query.Options.Rerank);
        Assert.StartsWith("idx-", query.IndexId);
    }

    [Fact]
    public async Task Ask_Returns_Both_Messages_With_Citation_Snapshot()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var result = (await (await AskAsync(s.Client, s.ConversationId, Question, topK: 5))
            .Content.ReadFromJsonAsync<AskResultDto>())!;

        Assert.Equal("user", result.UserMessage.Role);
        Assert.Equal(Question, result.UserMessage.Content);
        Assert.Empty(result.UserMessage.Citations);

        var a = result.AssistantMessage;
        Assert.Equal("assistant", a.Role);
        Assert.Equal(RecordingRagClient.DefaultAnswer.Answer, a.Content);
        Assert.Equal(812, a.LatencyMs);
        Assert.Null(a.ErrorCode);
        Assert.Equal(2, a.Citations.Count);
        Assert.Equal(5, rag.Queries.Single().TopK);

        var first = a.Citations[0];
        Assert.Equal(new CitationDto(1, "src/Foo.cs", "Handle", "method", 10, 24, "public void Handle() { }", 0.91,
            s.RepositoryId, "abc123"), first);
    }

    [Fact]
    public async Task History_Returns_Messages_In_Order_And_Auto_Titles_The_Conversation()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        await AskAsync(s.Client, s.ConversationId, "first question");
        await AskAsync(s.Client, s.ConversationId, "second question");

        var history = await HistoryAsync(s.Client, s.ConversationId);
        Assert.Equal(["user", "assistant", "user", "assistant"], history.Select(m => m.Role));
        Assert.Equal("first question", history[0].Content);
        Assert.Equal("second question", history[2].Content);
        Assert.Equal(history.OrderBy(m => m.CreatedAt).Select(m => m.Id), history.Select(m => m.Id));

        var list = await (await s.Client.GetAsync($"/api/projects/{s.ProjectId}/conversations"))
            .Content.ReadFromJsonAsync<List<ConversationSummaryDto>>();
        Assert.Equal("first question", list!.Single().Title); // set by the first question, not overwritten by the second
    }

    [Fact]
    public async Task An_Explicit_Title_Is_Not_Overwritten_By_The_First_Question()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);
        var titled = await CreateConversationAsync(s.Client, s.ProjectId, "My title");

        await AskAsync(s.Client, titled.Id);

        var list = await (await s.Client.GetAsync($"/api/projects/{s.ProjectId}/conversations"))
            .Content.ReadFromJsonAsync<List<ConversationSummaryDto>>();
        Assert.Contains(list!, c => c.Id == titled.Id && c.Title == "My title");
    }

    [Fact]
    public async Task Long_Multiline_Questions_Produce_A_Short_Single_Line_Title()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        await AskAsync(s.Client, s.ConversationId, "line one\n\nline two " + new string('x', 200));

        var list = await (await s.Client.GetAsync($"/api/projects/{s.ProjectId}/conversations"))
            .Content.ReadFromJsonAsync<List<ConversationSummaryDto>>();
        var title = list!.Single().Title;
        Assert.True(title.Length <= ChatRules.MaxAutoTitleLength);
        Assert.DoesNotContain('\n', title);
        Assert.StartsWith("line one line two", title);
    }

    [Fact]
    public async Task History_Limit_Returns_The_Most_Recent_Messages()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);
        await AskAsync(s.Client, s.ConversationId, "q1");
        await AskAsync(s.Client, s.ConversationId, "q2");

        var last = await (await s.Client.GetAsync($"/api/conversations/{s.ConversationId}/messages?limit=2"))
            .Content.ReadFromJsonAsync<List<MessageDto>>();

        Assert.Equal(["q2", RecordingRagClient.DefaultAnswer.Answer], last!.Select(m => m.Content));
    }

    [Fact]
    public async Task Content_Is_Stored_Verbatim_Without_Mutation_Or_Escaping()
    {
        var rag = new RecordingRagClient
        {
            OnQuery = _ => new RagAnswer("<img src=x onerror=alert(1)> **bold** [1]",
                [new RagCitation(1, "a.cs", null, null, 1, 1, "<script>alert('x')</script>", null)], null, 5),
        };
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);
        const string odd = "Tại sao <b>này</b> lại lỗi? 你好 😀";

        await AskAsync(s.Client, s.ConversationId, odd);
        var history = await HistoryAsync(s.Client, s.ConversationId);

        // The backend stores raw text; escaping happens when the frontend renders it (D22).
        Assert.Equal(odd, history[0].Content);
        Assert.Equal("<img src=x onerror=alert(1)> **bold** [1]", history[1].Content);
        Assert.Equal("<script>alert('x')</script>", history[1].Citations.Single().Snippet);
    }

    // ---------- citation snapshot survives RAG (D14, "done when") ----------

    [Fact]
    public async Task Citations_Are_Read_Back_Intact_After_The_Repository_And_RAG_Index_Are_Deleted()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);
        var asked = (await (await AskAsync(s.Client, s.ConversationId)).Content.ReadFromJsonAsync<AskResultDto>())!;
        var before = await HistoryAsync(s.Client, s.ConversationId);

        // RAG drops the index and the repository row is removed.
        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.DeleteAsync($"/api/repositories/{s.RepositoryId}")).StatusCode);
        Assert.Single(rag.Deleted);

        var after = await HistoryAsync(s.Client, s.ConversationId);

        Assert.Equal(before.Count, after.Count);
        Assert.Equal(asked.AssistantMessage.Citations, after[1].Citations);
        Assert.Equal(asked.AssistantMessage.Content, after[1].Content);
        var c = after[1].Citations[0];
        Assert.Equal("public void Handle() { }", c.Snippet);   // enough to render when the file can no longer be fetched
        Assert.Equal(s.RepositoryId, c.RepositoryId);
        Assert.Equal("abc123", c.CommitSha);
    }

    [Fact]
    public async Task A_Later_Index_For_The_Same_Project_Does_Not_Rewrite_Old_Snapshots()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);
        await AskAsync(s.Client, s.ConversationId);
        var before = (await HistoryAsync(s.Client, s.ConversationId))[1].Citations;

        // A newer import with a different commit becomes the active repository.
        rag.OnGet = id => RecordingRagClient.Ready(id) with { CommitSha = "ffff999" };
        var second = (await (await s.Client.PostAsync($"/api/projects/{s.ProjectId}/repositories",
            new { repoUrl = "https://github.com/octo/hello", @ref = "develop" })).Content.ReadFromJsonAsync<RepositoryDto>())!;
        await s.Client.GetAsync($"/api/repositories/{second.Id}");
        await AskAsync(s.Client, s.ConversationId, "again");

        var history = await HistoryAsync(s.Client, s.ConversationId);
        Assert.Equal(before, history[1].Citations);
        Assert.All(history[3].Citations, c =>
        {
            Assert.Equal(second.Id, c.RepositoryId);
            Assert.Equal("ffff999", c.CommitSha);
        });
    }

    // ---------- blocked until the repository is ready ----------

    [Fact]
    public async Task Ask_Is_Blocked_With_409_While_The_Index_Is_Still_Building()
    {
        var rag = new RecordingRagClient(); // GET status keeps answering "embedding"
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ready: false);

        var response = await AskAsync(s.Client, s.ConversationId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemWithCode>();
        Assert.Equal("INDEX_NOT_READY", problem!.Code);
        Assert.Empty(rag.Queries);
        Assert.Empty(await HistoryAsync(s.Client, s.ConversationId)); // nothing recorded for a refused question
    }

    [Fact]
    public async Task Ask_Refreshes_A_Stale_Status_So_Users_Need_Not_Poll_First()
    {
        var rag = new RecordingRagClient { OnGet = RecordingRagClient.Ready };
        using var app = WithRag(rag);
        // The stored status is still "pending" although RAG has finished.
        var s = await SetupAsync(app, rag, ready: false);

        var response = await AskAsync(s.Client, s.ConversationId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(rag.Queries);
    }

    [Fact]
    public async Task Ask_Is_Blocked_When_The_Repository_Failed()
    {
        var rag = new RecordingRagClient
        {
            OnGet = id => new RagIndexStatus(id, IndexStatus.Failed, "failed", 0, 0, null, null, null,
                new RagIndexError("REPO_TOO_LARGE", null)),
        };
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ready: false);

        var response = await AskAsync(s.Client, s.ConversationId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("REPOSITORY_FAILED", (await response.Content.ReadFromJsonAsync<ProblemWithCode>())!.Code);
        Assert.Empty(rag.Queries);
    }

    [Fact]
    public async Task Ask_Is_Blocked_When_The_Project_Has_No_Repository()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = (await (await client.PostAsync("/api/projects", new { name = "empty" })).Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var conversation = await CreateConversationAsync(client, project.Id);

        var response = await AskAsync(client, conversation.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NO_REPOSITORY", (await response.Content.ReadFromJsonAsync<ProblemWithCode>())!.Code);
        Assert.Empty(rag.Queries);
    }

    // ---------- input limits (D22) ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    \n\t ")]
    public async Task Ask_Rejects_Blank_Questions(string? question)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var response = await AskAsync(s.Client, s.ConversationId, question);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(rag.Queries);
    }

    [Fact]
    public async Task Ask_Enforces_The_Question_Length_Limit()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var tooLong = await AskAsync(s.Client, s.ConversationId, new string('q', ChatRules.MaxQuestionLength + 1));
        var atLimit = await AskAsync(s.Client, s.ConversationId, new string('q', ChatRules.MaxQuestionLength));

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
        Assert.Single(rag.Queries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ChatRules.MaxTopK + 1)]
    public async Task Ask_Enforces_The_TopK_Range(int topK)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var response = await AskAsync(s.Client, s.ConversationId, Question, topK);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(rag.Queries);
    }

    [Fact]
    public async Task Ask_Accepts_The_TopK_Boundaries()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        Assert.Equal(HttpStatusCode.Created, (await AskAsync(s.Client, s.ConversationId, Question, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AskAsync(s.Client, s.ConversationId, Question, ChatRules.MaxTopK)).StatusCode);
    }

    // ---------- RAG failures ----------

    [Theory]
    [InlineData(RagFailureKind.LlmError, "LLM_ERROR", 502)]
    [InlineData(RagFailureKind.Timeout, "TIMEOUT", 504)]
    [InlineData(RagFailureKind.Unavailable, "RAG_UNAVAILABLE", 502)]
    [InlineData(RagFailureKind.Internal, "INTERNAL_ERROR", 502)]
    public async Task Infrastructure_Failures_Are_Returned_And_Recorded_As_A_Failed_Turn(RagFailureKind kind, string code, int status)
    {
        var rag = new RecordingRagClient { QueryError = RecordingRagClient.Error(kind, code) };
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var response = await AskAsync(s.Client, s.ConversationId);

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var history = await HistoryAsync(s.Client, s.ConversationId);
        Assert.Equal(2, history.Count);
        Assert.Equal(Question, history[0].Content);
        Assert.Equal("assistant", history[1].Role);
        Assert.Equal("", history[1].Content);
        Assert.Equal(code, history[1].ErrorCode);
        Assert.Empty(history[1].Citations);
    }

    [Fact]
    public async Task The_Error_Response_Uses_The_Mapped_Code_And_Does_Not_Leak_Internals()
    {
        var rag = new RecordingRagClient
        {
            QueryError = new RagException(RagFailureKind.LlmError, "LLM_ERROR", 502, "upstream said: secret internal detail"),
        };
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var response = await AskAsync(s.Client, s.ConversationId);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("LLM_ERROR", (await response.Content.ReadFromJsonAsync<ProblemWithCode>())!.Code);
        Assert.DoesNotContain("secret internal detail", body);
    }

    [Fact]
    public async Task A_Retry_After_A_Failure_Succeeds_And_History_Shows_Both_Attempts()
    {
        var rag = new RecordingRagClient { QueryError = RecordingRagClient.Error(RagFailureKind.Timeout, "TIMEOUT") };
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);
        await AskAsync(s.Client, s.ConversationId);

        rag.QueryError = null;
        var retry = await AskAsync(s.Client, s.ConversationId);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var history = await HistoryAsync(s.Client, s.ConversationId);
        Assert.Equal(["TIMEOUT", null], history.Where(m => m.Role == "assistant").Select(m => m.ErrorCode));
    }

    [Fact]
    public async Task RAG_Rejecting_The_Request_Is_Not_Recorded_As_A_Turn()
    {
        var rag = new RecordingRagClient { QueryError = RecordingRagClient.Error(RagFailureKind.Invalid, "INVALID_REPO") };
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var response = await AskAsync(s.Client, s.ConversationId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(await HistoryAsync(s.Client, s.ConversationId));
    }

    [Fact]
    public async Task If_RAG_Says_The_Index_Is_Gone_The_Repository_Is_Marked_Failed()
    {
        var rag = new RecordingRagClient { QueryError = RecordingRagClient.Error(RagFailureKind.NotFound, "INDEX_NOT_FOUND") };
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        var first = await AskAsync(s.Client, s.ConversationId);
        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);

        var repo = (await (await s.Client.GetAsync($"/api/repositories/{s.RepositoryId}")).Content.ReadFromJsonAsync<RepositoryDto>())!;
        Assert.Equal("failed", repo.Status);
        Assert.Equal("INDEX_NOT_FOUND", repo.ErrorCode);

        var second = await AskAsync(s.Client, s.ConversationId); // now refused up front, without calling RAG again
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single(rag.Queries);
    }

    // ---------- tenant isolation ----------

    [Fact]
    public async Task User_B_Cannot_See_Create_Read_Or_Ask_In_User_As_Conversations()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var a = await SetupAsync(app, rag);
        await AskAsync(a.Client, a.ConversationId, "A's private question");
        var queriesBefore = rag.Queries.Count;
        var b = await AuthTestClient.NewUserAsync(app);

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/projects/{a.ProjectId}/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await b.PostAsync($"/api/projects/{a.ProjectId}/conversations", new { title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/conversations/{a.ConversationId}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AskAsync(b, a.ConversationId, "hijack")).StatusCode);

        Assert.Equal(queriesBefore, rag.Queries.Count); // B never reached RAG through A's index
        var history = await HistoryAsync(a.Client, a.ConversationId);
        Assert.Equal(2, history.Count);
        Assert.DoesNotContain(history, m => m.Content == "hijack");
    }

    [Fact]
    public async Task Someone_Elses_Conversation_Looks_Exactly_Like_A_Missing_One()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var a = await SetupAsync(app, rag);
        var b = await AuthTestClient.NewUserAsync(app);

        var other = await b.GetAsync($"/api/conversations/{a.ConversationId}/messages");
        var missing = await b.GetAsync($"/api/conversations/{Guid.NewGuid()}/messages");

        Assert.Equal(missing.StatusCode, other.StatusCode);
        var o = await other.Content.ReadFromJsonAsync<ProblemDetails>();
        var m = await missing.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(m!.Title, o!.Title);
        Assert.Equal(m.Detail, o.Detail);
    }

    [Fact]
    public async Task Deleting_A_Project_Removes_Its_Conversations_And_Messages()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);
        await AskAsync(s.Client, s.ConversationId);

        await s.Client.DeleteAsync($"/api/projects/{s.ProjectId}");

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/conversations/{s.ConversationId}/messages")).StatusCode);
    }

    // ---------- the whole flow on the in-memory fake RAG (no test double) ----------

    [Fact]
    public async Task Full_Flow_On_The_Fake_RAG_Import_Poll_Ask_And_Read_History()
    {
        var client = await AuthTestClient.NewUserAsync(factory);
        var project = (await (await client.PostAsync("/api/projects", new { name = "e2e" })).Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var repo = (await (await client.PostAsync($"/api/projects/{project.Id}/repositories",
            new { repoUrl = "https://github.com/octo/hello" })).Content.ReadFromJsonAsync<RepositoryDto>())!;
        var conversation = await CreateConversationAsync(client, project.Id);

        // Not ready yet: the first ask refreshes status (still building) and is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await AskAsync(client, conversation.Id)).StatusCode);

        for (var i = 0; i < 10; i++)
        {
            var r = (await (await client.GetAsync($"/api/repositories/{repo.Id}")).Content.ReadFromJsonAsync<RepositoryDto>())!;
            if (r.Status == "ready") break;
        }

        var response = await AskAsync(client, conversation.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var history = await HistoryAsync(client, conversation.Id);
        Assert.Equal(2, history.Count);
        Assert.Contains("[1]", history[1].Content);
        var citation = Assert.Single(history[1].Citations);
        Assert.Equal("src/Program.cs", citation.FilePath);
        Assert.Equal(repo.Id, citation.RepositoryId);
        Assert.NotNull(citation.CommitSha);
    }

    // ---------- the question and the answer are never logged ----------

    [Fact]
    public async Task Questions_And_Answers_Never_Appear_In_Logs_Even_When_Things_Fail()
    {
        const string secretQuestion = "SECRET-QUESTION-9f3a how do I reach the vault?";
        const string secretAnswer = "SECRET-ANSWER-77b2 the vault is in module X";
        var rag = new RecordingRagClient
        {
            OnQuery = _ => new RagAnswer(secretAnswer,
                [new RagCitation(1, "a.cs", null, null, 1, 1, "SECRET-SNIPPET-1d4c", null)], null, 3),
        };
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);

        await AskAsync(s.Client, s.ConversationId, secretQuestion);
        rag.QueryError = RecordingRagClient.Error(RagFailureKind.LlmError, "LLM_ERROR");
        await AskAsync(s.Client, s.ConversationId, secretQuestion);
        rag.QueryError = RecordingRagClient.Error(RagFailureKind.NotFound, "INDEX_NOT_FOUND");
        await AskAsync(s.Client, s.ConversationId, secretQuestion);
        await AskAsync(s.Client, s.ConversationId, secretQuestion + new string('x', 5000)); // validation failure path

        var logs = string.Join("\n", factory.Logs.Entries);
        Assert.NotEmpty(logs);
        Assert.DoesNotContain("SECRET-QUESTION-9f3a", logs);
        Assert.DoesNotContain("SECRET-ANSWER-77b2", logs);
        Assert.DoesNotContain("SECRET-SNIPPET-1d4c", logs);
    }
}
