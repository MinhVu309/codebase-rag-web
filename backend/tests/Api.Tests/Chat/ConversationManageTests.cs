using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Projects;
using Application.Chat;
using Application.Projects;
using Application.Repositories;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Chat;

/// <summary>Rename (PATCH) and hard delete (DELETE) of <c>/api/conversations/{id}</c>.</summary>
[Collection(ApiCollection.Name)]
public class ConversationManageTests(ApiFactory factory)
{
    private record Problem(int Status, string? Title, string? Detail, string? Code, Dictionary<string, string[]>? Errors);

    private WebApplicationFactory<Program> WithRag(RecordingRagClient rag) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<Application.Rag.IRagClient>(rag)));

    private sealed record Setup(AuthTestClient Client, Guid ProjectId, Guid ConversationId);

    /// <summary>A user with a project (repository ready), one conversation and, optionally, one answered question.</summary>
    private static async Task<Setup> SetupAsync(WebApplicationFactory<Program> app, RecordingRagClient rag, bool ask = true)
    {
        var client = await AuthTestClient.NewUserAsync(app);
        var project = (await (await client.PostAsync("/api/projects", new { name = "p" }))
            .Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var repo = (await (await client.PostAsync($"/api/projects/{project.Id}/repositories",
            new { repoUrl = "https://github.com/octo/hello" })).Content.ReadFromJsonAsync<RepositoryDto>())!;
        rag.OnGet = RecordingRagClient.Ready;
        await client.GetAsync($"/api/repositories/{repo.Id}");
        rag.OnGet = null;

        var conversation = await NewConversationAsync(client, project.Id);
        if (ask) await AskAsync(client, conversation.Id);
        return new Setup(client, project.Id, conversation.Id);
    }

    private static async Task<ConversationDto> NewConversationAsync(AuthTestClient client, Guid projectId, string? title = null)
    {
        var response = await client.PostAsync($"/api/projects/{projectId}/conversations", new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConversationDto>())!;
    }

    private static async Task AskAsync(AuthTestClient client, Guid conversationId)
    {
        var response = await client.PostAsync($"/api/conversations/{conversationId}/messages", new { question = "Where is it?" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<List<ConversationSummaryDto>> ListAsync(AuthTestClient client, Guid projectId) =>
        (await (await client.GetAsync($"/api/projects/{projectId}/conversations")).Content
            .ReadFromJsonAsync<List<ConversationSummaryDto>>())!;

    private static async Task<int> MessageCountAsync(WebApplicationFactory<Program> app, Guid conversationId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Messages.CountAsync(m => m.ConversationId == conversationId);
    }

    private static async Task<bool> ConversationExistsAsync(WebApplicationFactory<Program> app, Guid conversationId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Conversations.AnyAsync(c => c.Id == conversationId);
    }

    private static async Task<Problem> ProblemOf(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<Problem>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    // ---------- authentication ----------

    [Theory]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Endpoints_Require_Authentication(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/conversations/00000000-0000-0000-0000-000000000001");
        if (method == "PATCH") request.Content = JsonContent.Create(new { title = "x" });

        var response = await factory.CreateAuthClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- rename ----------

    [Fact]
    public async Task Rename_Returns_The_Updated_Conversation_And_The_List_Shows_The_New_Title()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);
        var before = Assert.Single(await ListAsync(s.Client, s.ProjectId));

        var response = await s.Client.PatchAsync($"/api/conversations/{s.ConversationId}", new { title = "Auth flow notes" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["createdAt", "id", "projectId", "title"], json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        var dto = json.RootElement.Deserialize<ConversationDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal((s.ConversationId, s.ProjectId, "Auth flow notes", before.CreatedAt), (dto.Id, dto.ProjectId, dto.Title, dto.CreatedAt));

        var after = Assert.Single(await ListAsync(s.Client, s.ProjectId));
        Assert.Equal("Auth flow notes", after.Title);
        Assert.Equal(before.MessageCount, after.MessageCount); // messages untouched
    }

    [Fact]
    public async Task Rename_Trims_Both_Ends_And_Stores_The_Trimmed_Title()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ask: false);

        var response = await s.Client.PatchAsync($"/api/conversations/{s.ConversationId}", new { title = "  \t Spaced out \n " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Spaced out", (await response.Content.ReadFromJsonAsync<ConversationDto>())!.Title);
        Assert.Equal("Spaced out", Assert.Single(await ListAsync(s.Client, s.ProjectId)).Title);
    }

    [Fact]
    public async Task Rename_Accepts_Exactly_200_Characters_Even_With_Padding_Around_Them()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ask: false);
        var title = new string('x', 200);

        var plain = await s.Client.PatchAsync($"/api/conversations/{s.ConversationId}", new { title });
        var padded = await s.Client.PatchAsync($"/api/conversations/{s.ConversationId}", new { title = $"   {title}   " });

        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, padded.StatusCode);
        Assert.Equal(title, (await padded.Content.ReadFromJsonAsync<ConversationDto>())!.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t\n ")]
    [InlineData("a\nb")]            // line break inside
    [InlineData("a\r\nb")]
    [InlineData("a\tb")]
    [InlineData("a\0b")]
    [InlineData("a\u007Fb")]
    public async Task Rename_Rejects_Blank_Or_Control_Character_Titles(string title)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ask: false);
        var original = Assert.Single(await ListAsync(s.Client, s.ProjectId)).Title;

        var response = await s.Client.PatchAsync($"/api/conversations/{s.ConversationId}", new { title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Title", (await ProblemOf(response)).Errors!.Keys);
        Assert.Equal(original, Assert.Single(await ListAsync(s.Client, s.ProjectId)).Title);
    }

    [Fact]
    public async Task Rename_Rejects_201_Characters()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ask: false);

        var response = await s.Client.PatchAsync($"/api/conversations/{s.ConversationId}", new { title = new string('x', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Title", (await ProblemOf(response)).Errors!.Keys);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":null}")]
    public async Task Rename_Requires_A_Title(string json)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ask: false);

        var response = await SendRawPatchAsync(s.Client, $"/api/conversations/{s.ConversationId}", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Title", (await ProblemOf(response)).Errors!.Keys);
    }

    [Fact]
    public async Task Rename_Without_A_Body_Is_400_On_Your_Own_Conversation()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ask: false);

        var response = await s.Client.PatchAsync($"/api/conversations/{s.ConversationId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendRawPatchAsync(AuthTestClient client, string url, string json) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    // ---------- ownership ----------

    [Fact]
    public async Task Someone_Elses_Conversation_Looks_Exactly_Like_One_That_Does_Not_Exist_And_Is_Untouched()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var owner = await SetupAsync(app, rag);
        var intruder = await AuthTestClient.NewUserAsync(app);
        var url = $"/api/conversations/{owner.ConversationId}";
        var missingUrl = $"/api/conversations/{Guid.NewGuid()}";
        var messagesBefore = await MessageCountAsync(app, owner.ConversationId);
        var titleBefore = Assert.Single(await ListAsync(owner.Client, owner.ProjectId)).Title;

        var foreignPatch = await intruder.PatchAsync(url, new { title = "hijacked" });
        var missingPatch = await intruder.PatchAsync(missingUrl, new { title = "hijacked" });
        var foreignDelete = await intruder.DeleteAsync(url);
        var missingDelete = await intruder.DeleteAsync(missingUrl);

        foreach (var r in new[] { foreignPatch, missingPatch, foreignDelete, missingDelete })
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        var (a, b) = (await ProblemOf(foreignPatch), await ProblemOf(missingPatch));
        Assert.Equal((a.Status, a.Title, a.Detail, a.Code), (b.Status, b.Title, b.Detail, b.Code));
        var (c, d) = (await ProblemOf(foreignDelete), await ProblemOf(missingDelete));
        Assert.Equal((c.Status, c.Title, c.Detail, c.Code), (d.Status, d.Title, d.Detail, d.Code));

        Assert.Equal(titleBefore, Assert.Single(await ListAsync(owner.Client, owner.ProjectId)).Title);
        Assert.Equal(messagesBefore, await MessageCountAsync(app, owner.ConversationId));
        Assert.True(await ConversationExistsAsync(app, owner.ConversationId));
    }

    [Theory]
    [InlineData("{\"title\":\"\"}")]               // blank
    [InlineData("{\"title\":\"a\\nb\"}")]          // control character
    [InlineData("{}")]                             // missing
    [InlineData("")]                               // no body at all
    public async Task A_Bad_Body_On_Someone_Elses_Conversation_Is_Still_404_Not_400(string json)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var owner = await SetupAsync(app, rag, ask: false);
        var intruder = await AuthTestClient.NewUserAsync(app);

        var response = json.Length == 0
            ? await intruder.PatchAsync($"/api/conversations/{owner.ConversationId}")
            : await SendRawPatchAsync(intruder, $"/api/conversations/{owner.ConversationId}", json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- delete ----------

    [Fact]
    public async Task Delete_Removes_The_Conversation_Its_Messages_And_Citations_And_Leaves_Others_Alone()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag);                        // conversation 1: user + assistant message (with citations)
        var other = await NewConversationAsync(s.Client, s.ProjectId, "Keep me");
        await AskAsync(s.Client, other.Id);                        // conversation 2: also answered
        Assert.Equal(2, await MessageCountAsync(app, s.ConversationId));
        Assert.Equal(2, await MessageCountAsync(app, other.Id));
        var withCitations = (await (await s.Client.GetAsync($"/api/conversations/{s.ConversationId}/messages"))
            .Content.ReadFromJsonAsync<List<MessageDto>>())!;
        Assert.Contains(withCitations, m => m.Citations.Count > 0); // there really was a snapshot to delete

        var response = await s.Client.DeleteAsync($"/api/conversations/{s.ConversationId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/conversations/{s.ConversationId}/messages")).StatusCode);
        Assert.False(await ConversationExistsAsync(app, s.ConversationId));
        Assert.Equal(0, await MessageCountAsync(app, s.ConversationId));

        var remaining = Assert.Single(await ListAsync(s.Client, s.ProjectId));
        Assert.Equal((other.Id, "Keep me", 2), (remaining.Id, remaining.Title, remaining.MessageCount));
        Assert.Equal(2, await MessageCountAsync(app, other.Id));
        var kept = (await (await s.Client.GetAsync($"/api/conversations/{other.Id}/messages"))
            .Content.ReadFromJsonAsync<List<MessageDto>>())!;
        Assert.Contains(kept, m => m.Citations.Count > 0);        // the other conversation's snapshots are intact
        Assert.Empty(rag.Deleted);                                 // RAG is not involved
    }

    [Fact]
    public async Task Deleting_Twice_Is_404_The_Second_Time()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ask: false);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.DeleteAsync($"/api/conversations/{s.ConversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.DeleteAsync($"/api/conversations/{s.ConversationId}")).StatusCode);
    }

    [Fact]
    public async Task Renaming_A_Deleted_Conversation_Is_404()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ask: false);
        await s.Client.DeleteAsync($"/api/conversations/{s.ConversationId}");

        var response = await s.Client.PatchAsync($"/api/conversations/{s.ConversationId}", new { title = "back from the dead" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Asking_In_A_Deleted_Conversation_Is_404_And_Does_Not_Call_RAG()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ask: false);
        await s.Client.DeleteAsync($"/api/conversations/{s.ConversationId}");

        var response = await s.Client.PostAsync($"/api/conversations/{s.ConversationId}/messages", new { question = "Anyone there?" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(rag.Queries);
        Assert.Equal(0, await MessageCountAsync(app, s.ConversationId));
    }

    // ---------- D22: user content is not logged ----------

    [Fact]
    public async Task Titles_Are_Never_Logged()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var s = await SetupAsync(app, rag, ask: false);

        await s.Client.PatchAsync($"/api/conversations/{s.ConversationId}", new { title = "SECRET-TITLE-5521" });
        await s.Client.PatchAsync($"/api/conversations/{s.ConversationId}", new { title = "SECRET-TITLE-5521\nbad" });
        await s.Client.DeleteAsync($"/api/conversations/{s.ConversationId}");

        var logs = string.Join("\n", factory.Logs.Entries);
        Assert.NotEmpty(logs);
        Assert.DoesNotContain("SECRET-TITLE-5521", logs);
    }
}
