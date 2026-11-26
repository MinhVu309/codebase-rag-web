using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Projects;
using Application.Chat;
using Application.Projects;
using Application.Repositories;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Api.Tests.Security.SecurityTestSupport;

namespace Api.Tests.Chat;

/// <summary>
/// Conversation Scope (D40, S1): <c>initialRepositoryId</c> on create and the three <c>/repositories</c> endpoints.
/// The question flow is unchanged here (it still answers from the project's active index).
/// </summary>
[Collection(ApiCollection.Name)]
public class ConversationScopeTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private int _repoCounter;

    private record Problem(int Status, string? Title, string? Detail, string? Code, Dictionary<string, string[]>? Errors);

    private static async Task<Problem> ProblemOf(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<Problem>(Web))!;

    private sealed class Env(WebApplicationFactory<Program> app, RecordingRagClient rag, AuthTestClient client, Guid projectId)
    {
        public WebApplicationFactory<Program> App { get; } = app;
        public RecordingRagClient Rag { get; } = rag;
        public AuthTestClient Client { get; } = client;
        public Guid ProjectId { get; } = projectId;
    }

    private async Task<Env> NewEnvAsync(params (string Key, string? Value)[] settings)
    {
        var rag = new RecordingRagClient();
        var app = factory.With(rag, settings);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = (await (await client.PostAsync("/api/projects", new { name = "p" })).Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        return new Env(app, rag, client, project.Id);
    }

    /// <summary>Imports a repository into <paramref name="projectId"/>; with <paramref name="ready"/> its stored status becomes ready.</summary>
    private async Task<Guid> ImportAsync(Env env, bool ready = true, Guid? projectId = null, AuthTestClient? as_ = null)
    {
        var client = as_ ?? env.Client;
        var pid = projectId ?? env.ProjectId;
        var n = Interlocked.Increment(ref _repoCounter);
        var response = await client.PostAsync($"/api/projects/{pid}/repositories", new { repoUrl = $"https://github.com/octo/repo-{n}" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var repo = (await response.Content.ReadFromJsonAsync<RepositoryDto>())!;
        if (ready)
        {
            env.Rag.OnGet = RecordingRagClient.Ready;
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/repositories/{repo.Id}")).StatusCode);
            env.Rag.OnGet = null;
        }
        return repo.Id;
    }

    private static async Task<Guid> CreateConversationAsync(Env env, Guid? initial, string? title = "t")
    {
        var response = await env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", new { title, initialRepositoryId = initial });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConversationDto>())!.Id;
    }

    private static async Task<List<JsonElement>> ScopeOf(AuthTestClient client, Guid conversationId)
    {
        var response = await client.GetAsync($"/api/conversations/{conversationId}/repositories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<JsonElement>>())!;
    }

    private static List<Guid> Ids(IEnumerable<JsonElement> scope) => scope.Select(e => e.GetProperty("repositoryId").GetGuid()).ToList();

    private static async Task<T> InDb<T>(WebApplicationFactory<Program> app, Func<AppDbContext, Task<T>> query)
    {
        using var scope = app.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<int> MembersInDb(Env env, Guid conversationId) =>
        InDb(env.App, db => db.ConversationRepositories.CountAsync(x => x.ConversationId == conversationId));

    // =========================================================================================================
    // create: initialRepositoryId
    // =========================================================================================================

    [Fact]
    public async Task Create_With_A_Ready_Initial_Repository_Makes_The_Conversation_And_Its_First_Member()
    {
        var env = await NewEnvAsync();
        var repo = await ImportAsync(env);

        var response = await env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", new { title = "hello", initialRepositoryId = repo });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // the DTO keeps its old shape: no new field, in particular no scope and no index id
        Assert.Equal(["id", "projectId", "title", "createdAt"], body.RootElement.EnumerateObject().Select(p => p.Name));
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal([repo], Ids(await ScopeOf(env.Client, id)));

        var row = await InDb(env.App, db => db.ConversationRepositories.SingleAsync(x => x.ConversationId == id));
        var created = body.RootElement.GetProperty("createdAt").GetDateTimeOffset();
        Assert.Equal(created, row.AddedAt);
        Assert.NotNull(row.AddedByUserId);
    }

    [Fact]
    public async Task Create_With_An_Initial_Repository_That_Is_Not_Ready_Is_409_And_Creates_Nothing()
    {
        var env = await NewEnvAsync();
        var pending = await ImportAsync(env, ready: false);

        var response = await env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", new { title = "x", initialRepositoryId = pending });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("REPOSITORY_NOT_READY", (await ProblemOf(response)).Code);
        var list = await env.Client.GetAsync($"/api/projects/{env.ProjectId}/conversations");
        Assert.Empty((await list.Content.ReadFromJsonAsync<List<ConversationSummaryDto>>())!);
    }

    [Fact]
    public async Task Create_With_A_Repository_Of_Another_Project_Another_User_Or_Nothing_Is_The_Same_404_As_A_Missing_Project()
    {
        var env = await NewEnvAsync();
        var otherProject = (await (await env.Client.PostAsync("/api/projects", new { name = "other" })).Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var inOtherProject = await ImportAsync(env, projectId: otherProject.Id);
        var stranger = await AuthTestClient.NewUserAsync(env.App);
        var strangerProject = (await (await stranger.PostAsync("/api/projects", new { name = "s" })).Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var strangers = await ImportAsync(env, projectId: strangerProject.Id, as_: stranger);

        var missingProject = await env.Client.PostAsync($"/api/projects/{Guid.NewGuid()}/conversations", new { title = "x", initialRepositoryId = Guid.NewGuid() });
        var expected = await ProblemOf(missingProject);
        Assert.Equal(404, expected.Status);

        foreach (var id in new[] { inOtherProject, strangers, Guid.NewGuid() })
        {
            var response = await env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", new { title = "x", initialRepositoryId = id });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var problem = await ProblemOf(response);
            Assert.Equal(expected.Title, problem.Title);
            Assert.Equal(expected.Detail, problem.Detail);
        }
        var list = await env.Client.GetAsync($"/api/projects/{env.ProjectId}/conversations");
        Assert.Empty((await list.Content.ReadFromJsonAsync<List<ConversationSummaryDto>>())!);
    }

    [Fact]
    public async Task A_404_For_The_Repository_Comes_Before_A_400_For_The_Body()
    {
        var env = await NewEnvAsync();

        var response = await env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations",
            new { title = "bad\ntitle", initialRepositoryId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Single_Mode_Without_An_Initial_Repository_Uses_The_Active_One_When_It_Is_Ready()
    {
        var env = await NewEnvAsync(); // RAG_QUERY_API defaults to single
        var older = await ImportAsync(env);
        var active = await ImportAsync(env); // the latest import is the project's active index

        var id = await CreateConversationAsync(env, null);

        Assert.Equal([active], Ids(await ScopeOf(env.Client, id)));
        Assert.NotEqual(older, active);
    }

    [Fact]
    public async Task Single_Mode_Initial_Repository_Wins_Over_The_Active_One()
    {
        var env = await NewEnvAsync();
        var first = await ImportAsync(env);
        await ImportAsync(env); // now the active one

        var id = await CreateConversationAsync(env, first);

        Assert.Equal([first], Ids(await ScopeOf(env.Client, id)));
    }

    [Fact]
    public async Task Single_Mode_With_No_Ready_Active_Repository_Starts_With_An_Empty_Scope()
    {
        var env = await NewEnvAsync();
        var noRepos = await CreateConversationAsync(env, null);
        await ImportAsync(env, ready: false); // active, but still indexing
        var notReady = await CreateConversationAsync(env, null);

        Assert.Empty(await ScopeOf(env.Client, noRepos));
        Assert.Empty(await ScopeOf(env.Client, notReady));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multi_Mode_Requires_An_Initial_Repository(bool explicitNull)
    {
        var env = await NewEnvAsync(("RAG_QUERY_API", "multi"));
        await ImportAsync(env); // an active ready repository does not help in multi mode
        object body = explicitNull ? new { title = "x", initialRepositoryId = (Guid?)null } : new { title = "x" };

        var response = await env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemOf(response);
        Assert.Contains("InitialRepositoryId", problem.Errors!.Keys);
    }

    [Fact]
    public async Task Multi_Mode_Reports_A_Bad_Title_And_A_Missing_Repository_Together_And_Accepts_A_Valid_Request()
    {
        var env = await NewEnvAsync(("RAG_QUERY_API", "multi"));
        var repo = await ImportAsync(env);

        var bad = await ProblemOf(await env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", new { title = "bad\ntitle" }));
        Assert.Contains("Title", bad.Errors!.Keys);
        Assert.Contains("InitialRepositoryId", bad.Errors.Keys);

        var id = await CreateConversationAsync(env, repo);
        Assert.Equal([repo], Ids(await ScopeOf(env.Client, id)));
    }

    [Fact]
    public async Task A_Malformed_Initial_Repository_Id_Is_A_400()
    {
        var env = await NewEnvAsync();

        var response = await env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", new { title = "x", initialRepositoryId = "not-a-guid" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =========================================================================================================
    // GET /api/conversations/{id}/repositories
    // =========================================================================================================

    [Fact]
    public async Task Get_Returns_The_Documented_Shape_With_Owner_Repo_Display_Names_Oldest_First()
    {
        var env = await NewEnvAsync();
        var first = await ImportAsync(env);
        var second = await ImportAsync(env);
        var third = await ImportAsync(env);
        var id = await CreateConversationAsync(env, first);
        Assert.Equal(HttpStatusCode.OK, (await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = third })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = second })).StatusCode);

        var response = await env.Client.GetAsync($"/api/conversations/{id}/repositories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scope = (await response.Content.ReadFromJsonAsync<List<JsonElement>>())!;
        Assert.Equal([first, third, second], Ids(scope)); // by addedAt, not by id
        var item = scope[0];
        Assert.Equal(["repositoryId", "displayName", "repoUrl", "status", "commitSha", "addedAt"], item.EnumerateObject().Select(p => p.Name));
        Assert.Equal(first.ToString(), item.GetProperty("repositoryId").GetString());
        Assert.StartsWith("octo/repo-", item.GetProperty("displayName").GetString());
        Assert.Equal("https://github.com/" + item.GetProperty("displayName").GetString(), item.GetProperty("repoUrl").GetString());
        Assert.Equal("ready", item.GetProperty("status").GetString());
        Assert.Equal("abc123", item.GetProperty("commitSha").GetString());
        Assert.True(item.GetProperty("addedAt").GetDateTimeOffset() <= scope[1].GetProperty("addedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Get_Shows_The_Current_Status_And_Null_Commit_Of_A_Member_That_Is_Not_Ready()
    {
        var env = await NewEnvAsync();
        var repo = await ImportAsync(env);
        var id = await CreateConversationAsync(env, repo);
        await InDb(env.App, async db =>
        {
            var r = await db.Repositories.SingleAsync(x => x.Id == repo);
            r.Status = IndexStatus.Failed;
            r.CommitSha = null;
            return await db.SaveChangesAsync();
        });

        var item = Assert.Single(await ScopeOf(env.Client, id));

        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("commitSha").ValueKind);
    }

    [Fact]
    public async Task Get_Of_An_Empty_Scope_Is_An_Empty_Array()
    {
        var env = await NewEnvAsync();
        var id = await CreateConversationAsync(env, null);

        var response = await env.Client.GetAsync($"/api/conversations/{id}/repositories");

        Assert.Equal("[]", (await response.Content.ReadAsStringAsync()).Trim());
    }

    // =========================================================================================================
    // POST /api/conversations/{id}/repositories
    // =========================================================================================================

    [Fact]
    public async Task Post_Adds_A_Ready_Repository_And_Returns_The_Scope_After_The_Add()
    {
        var env = await NewEnvAsync();
        var first = await ImportAsync(env);
        var second = await ImportAsync(env);
        var id = await CreateConversationAsync(env, first);

        var response = await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = second });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([first, second], Ids((await response.Content.ReadFromJsonAsync<List<JsonElement>>())!));
        var row = await InDb(env.App, db => db.ConversationRepositories.SingleAsync(x => x.RepositoryId == second));
        Assert.NotNull(row.AddedByUserId);
    }

    [Fact]
    public async Task Post_Of_A_Member_Is_Idempotent_200_And_Changes_Nothing()
    {
        var env = await NewEnvAsync();
        var first = await ImportAsync(env);
        var second = await ImportAsync(env);
        var id = await CreateConversationAsync(env, first);
        await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = second });
        var before = await InDb(env.App, db => db.ConversationRepositories.AsNoTracking().OrderBy(x => x.RepositoryId).ToListAsync());

        var again = await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = second });
        var firstAgain = await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = first });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstAgain.StatusCode);
        Assert.Equal(2, Ids((await again.Content.ReadFromJsonAsync<List<JsonElement>>())!).Count);
        var after = await InDb(env.App, db => db.ConversationRepositories.AsNoTracking().OrderBy(x => x.RepositoryId).ToListAsync());
        Assert.Equal(before.Select(x => (x.RepositoryId, x.AddedAt)), after.Select(x => (x.RepositoryId, x.AddedAt)));
    }

    [Fact]
    public async Task Post_Of_A_Repository_That_Is_Not_Ready_Is_409_And_The_Scope_Is_Unchanged()
    {
        var env = await NewEnvAsync();
        var first = await ImportAsync(env);
        var pending = await ImportAsync(env, ready: false);
        var id = await CreateConversationAsync(env, first);

        var response = await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = pending });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("REPOSITORY_NOT_READY", (await ProblemOf(response)).Code);
        Assert.Equal([first], Ids(await ScopeOf(env.Client, id)));
    }

    [Fact]
    public async Task Post_Of_A_Repository_From_Another_Project_Another_User_Or_Nothing_Is_404()
    {
        var env = await NewEnvAsync();
        var first = await ImportAsync(env);
        var id = await CreateConversationAsync(env, first);
        var otherProject = (await (await env.Client.PostAsync("/api/projects", new { name = "other" })).Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var inOtherProject = await ImportAsync(env, projectId: otherProject.Id); // same user, other project
        var stranger = await AuthTestClient.NewUserAsync(env.App);
        var strangerProject = (await (await stranger.PostAsync("/api/projects", new { name = "s" })).Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var strangers = await ImportAsync(env, projectId: strangerProject.Id, as_: stranger);

        var details = new List<string?>();
        foreach (var rid in new[] { inOtherProject, strangers, Guid.NewGuid() })
        {
            var response = await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = rid });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            details.Add((await ProblemOf(response)).Detail);
        }

        Assert.Single(details.Distinct()); // the three cases cannot be told apart
        Assert.Equal([first], Ids(await ScopeOf(env.Client, id)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"repositoryId":null}""")]
    [InlineData("")]
    public async Task Post_Without_A_Repository_Id_Is_400_With_An_Error_On_RepositoryId(string json)
    {
        var env = await NewEnvAsync();
        var id = await CreateConversationAsync(env, await ImportAsync(env));
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{id}/repositories")
        {
            Content = json.Length == 0 ? null : new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

        var response = await env.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("RepositoryId", (await ProblemOf(response)).Errors!.Keys);
    }

    [Fact]
    public async Task The_Cap_Is_Enforced_And_Leaves_The_Scope_Untouched()
    {
        var env = await NewEnvAsync(("CONVERSATION_SCOPE_MAX", "2"));
        var a = await ImportAsync(env);
        var b = await ImportAsync(env);
        var c = await ImportAsync(env);
        var id = await CreateConversationAsync(env, a);
        Assert.Equal(HttpStatusCode.OK, (await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = b })).StatusCode);
        var before = await InDb(env.App, db => db.ConversationRepositories.AsNoTracking().OrderBy(x => x.RepositoryId).Select(x => new { x.RepositoryId, x.AddedAt }).ToListAsync());

        var third = await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = c });

        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("SCOPE_LIMIT_REACHED", (await ProblemOf(third)).Code);
        var after = await InDb(env.App, db => db.ConversationRepositories.AsNoTracking().OrderBy(x => x.RepositoryId).Select(x => new { x.RepositoryId, x.AddedAt }).ToListAsync());
        Assert.Equal(before, after);
        Assert.Equal([a, b], Ids(await ScopeOf(env.Client, id)));

        // at the cap, re-adding a member is still the harmless 200
        Assert.Equal(HttpStatusCode.OK, (await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = b })).StatusCode);
        // and making room lets the third in
        Assert.Equal(HttpStatusCode.NoContent, (await env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{a}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = c })).StatusCode);
    }

    [Fact]
    public async Task The_Default_Cap_Is_Five()
    {
        var env = await NewEnvAsync();
        var repos = new List<Guid>();
        for (var i = 0; i < 6; i++) repos.Add(await ImportAsync(env));
        var id = await CreateConversationAsync(env, repos[0]);
        for (var i = 1; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = repos[i] })).StatusCode);

        var sixth = await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = repos[5] });

        Assert.Equal(HttpStatusCode.Conflict, sixth.StatusCode);
        Assert.Equal(5, await MembersInDb(env, id));
    }

    [Fact]
    public async Task Concurrent_Adds_Of_Different_Repositories_Cannot_Go_Past_The_Cap()
    {
        var env = await NewEnvAsync(("CONVERSATION_SCOPE_MAX", "2"));
        var first = await ImportAsync(env);
        var candidates = new List<Guid>();
        for (var i = 0; i < 6; i++) candidates.Add(await ImportAsync(env));
        var id = await CreateConversationAsync(env, first);

        var responses = await Task.WhenAll(candidates.Select(c =>
            env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = c })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2, await MembersInDb(env, id));
    }

    [Fact]
    public async Task Concurrent_Adds_Of_The_Same_Repository_Are_All_200_And_Make_One_Row()
    {
        var env = await NewEnvAsync();
        var first = await ImportAsync(env);
        var second = await ImportAsync(env);
        var id = await CreateConversationAsync(env, first);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = second })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(2, await MembersInDb(env, id));
    }

    [Fact]
    public async Task Concurrent_Removals_Cannot_Empty_The_Scope()
    {
        var env = await NewEnvAsync();
        var a = await ImportAsync(env);
        var b = await ImportAsync(env);
        var id = await CreateConversationAsync(env, a);
        await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = b });

        var responses = await Task.WhenAll(
            env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{a}"),
            env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{b}"));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await MembersInDb(env, id));
    }

    // =========================================================================================================
    // DELETE /api/conversations/{id}/repositories/{repositoryId}
    // =========================================================================================================

    [Fact]
    public async Task Delete_Removes_A_Member_But_Not_The_Repository()
    {
        var env = await NewEnvAsync();
        var a = await ImportAsync(env);
        var b = await ImportAsync(env);
        var id = await CreateConversationAsync(env, a);
        await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = b });

        var response = await env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{a}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([b], Ids(await ScopeOf(env.Client, id)));
        Assert.Equal(HttpStatusCode.OK, (await env.Client.GetAsync($"/api/repositories/{a}")).StatusCode);
        Assert.Empty(env.Rag.Deleted); // nothing was deleted in RAG either
    }

    [Fact]
    public async Task Delete_Of_The_Last_Member_Is_409_And_Keeps_It()
    {
        var env = await NewEnvAsync();
        var a = await ImportAsync(env);
        var id = await CreateConversationAsync(env, a);

        var response = await env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{a}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SCOPE_CANNOT_BE_EMPTY", (await ProblemOf(response)).Code);
        Assert.Equal([a], Ids(await ScopeOf(env.Client, id)));
    }

    [Fact]
    public async Task Delete_Of_Something_That_Is_Not_In_The_Scope_Is_404_Even_When_The_Scope_Has_One_Member()
    {
        var env = await NewEnvAsync();
        var a = await ImportAsync(env);
        var notMember = await ImportAsync(env); // exists in the project, not in this Scope
        var stranger = await AuthTestClient.NewUserAsync(env.App);
        var strangerProject = (await (await stranger.PostAsync("/api/projects", new { name = "s" })).Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var strangers = await ImportAsync(env, projectId: strangerProject.Id, as_: stranger);
        var id = await CreateConversationAsync(env, a);

        foreach (var rid in new[] { notMember, strangers, Guid.NewGuid() })
            Assert.Equal(HttpStatusCode.NotFound, (await env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{rid}")).StatusCode);

        Assert.Equal([a], Ids(await ScopeOf(env.Client, id)));
    }

    // =========================================================================================================
    // permissions
    // =========================================================================================================

    [Fact]
    public async Task Another_User_Gets_404_On_All_Three_Endpoints_And_Changes_Nothing()
    {
        var env = await NewEnvAsync();
        var a = await ImportAsync(env);
        var b = await ImportAsync(env);
        var id = await CreateConversationAsync(env, a);
        await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = b });
        var before = await InDb(env.App, db => db.ConversationRepositories.AsNoTracking().OrderBy(x => x.RepositoryId).Select(x => new { x.RepositoryId, x.AddedAt }).ToListAsync());

        var intruder = await AuthTestClient.NewUserAsync(env.App);
        var intruderProject = (await (await intruder.PostAsync("/api/projects", new { name = "mine" })).Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var ownRepo = await ImportAsync(env, projectId: intruderProject.Id, as_: intruder);

        var results = new[]
        {
            await intruder.GetAsync($"/api/conversations/{id}/repositories"),
            await intruder.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = ownRepo }),
            await intruder.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = a }),
            await intruder.PostAsync($"/api/conversations/{id}/repositories", new { }),   // bad body: still 404, not 400
            await intruder.DeleteAsync($"/api/conversations/{id}/repositories/{a}"),
            await intruder.DeleteAsync($"/api/conversations/{id}/repositories/{ownRepo}"),
        };
        var missing = await intruder.GetAsync($"/api/conversations/{Guid.NewGuid()}/repositories");

        Assert.All(results, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal((await ProblemOf(missing)).Detail, (await ProblemOf(results[0])).Detail);
        var after = await InDb(env.App, db => db.ConversationRepositories.AsNoTracking().OrderBy(x => x.RepositoryId).Select(x => new { x.RepositoryId, x.AddedAt }).ToListAsync());
        Assert.Equal(before, after);
        Assert.Equal(0, await InDb(env.App, db => db.ConversationRepositories.CountAsync(x => x.RepositoryId == ownRepo)));
    }

    [Fact]
    public async Task Anonymous_Calls_Are_401()
    {
        var env = await NewEnvAsync();
        var anonymous = new AuthTestClient(env.App);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/conversations/{id}/repositories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/conversations/{id}/repositories/{Guid.NewGuid()}")).StatusCode);
    }

    // =========================================================================================================
    // lifecycle: deleting a repository or a conversation
    // =========================================================================================================

    [Fact]
    public async Task Deleting_A_Repository_Removes_Its_Memberships_And_Keeps_The_Old_Messages_And_Citations()
    {
        var env = await NewEnvAsync();
        var a = await ImportAsync(env);
        var b = await ImportAsync(env); // the active one: the question below is answered from it (unchanged flow)
        var id = await CreateConversationAsync(env, a);
        await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = b });
        var asked = await env.Client.PostAsync($"/api/conversations/{id}/messages", new { question = "where is it?" });
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        var messagesBefore = await env.Client.GetAsync($"/api/conversations/{id}/messages");
        var historyBefore = await messagesBefore.Content.ReadAsStringAsync();

        var response = await env.Client.DeleteAsync($"/api/repositories/{b}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([a], Ids(await ScopeOf(env.Client, id)));
        Assert.Equal(0, await InDb(env.App, db => db.ConversationRepositories.CountAsync(x => x.RepositoryId == b)));
        var historyAfter = await (await env.Client.GetAsync($"/api/conversations/{id}/messages")).Content.ReadAsStringAsync();
        Assert.Equal(historyBefore, historyAfter);
        Assert.Contains("src/Foo.cs", historyAfter); // citation snapshot still there
    }

    [Fact]
    public async Task Deleting_The_Last_Member_Repository_Leaves_An_Empty_Scope_And_The_Conversation()
    {
        var env = await NewEnvAsync();
        var a = await ImportAsync(env);
        var id = await CreateConversationAsync(env, a);

        Assert.Equal(HttpStatusCode.NoContent, (await env.Client.DeleteAsync($"/api/repositories/{a}")).StatusCode);

        Assert.Empty(await ScopeOf(env.Client, id));
        var list = await env.Client.GetAsync($"/api/projects/{env.ProjectId}/conversations");
        Assert.Single((await list.Content.ReadFromJsonAsync<List<ConversationSummaryDto>>())!);
    }

    [Fact]
    public async Task Deleting_A_Conversation_Removes_Its_Memberships_Not_The_Repositories()
    {
        var env = await NewEnvAsync();
        var a = await ImportAsync(env);
        var id = await CreateConversationAsync(env, a);

        Assert.Equal(HttpStatusCode.NoContent, (await env.Client.DeleteAsync($"/api/conversations/{id}")).StatusCode);

        Assert.Equal(0, await MembersInDb(env, id));
        Assert.Equal(HttpStatusCode.OK, (await env.Client.GetAsync($"/api/repositories/{a}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_The_Project_Cleans_Up_Every_Membership()
    {
        var env = await NewEnvAsync();
        var a = await ImportAsync(env);
        var id = await CreateConversationAsync(env, a);

        Assert.Equal(HttpStatusCode.NoContent, (await env.Client.DeleteAsync($"/api/projects/{env.ProjectId}")).StatusCode);

        Assert.Equal(0, await MembersInDb(env, id));
    }

    [Fact]
    public async Task The_Question_Flow_Is_Unchanged_By_Scope_Membership()
    {
        // Still answered from the project's active index, whatever the Scope holds, and RAG_QUERY_API is not consulted.
        var env = await NewEnvAsync(("RAG_QUERY_API", "multi"));
        var a = await ImportAsync(env);
        var active = await ImportAsync(env);
        var id = await CreateConversationAsync(env, a);
        var indexOfActive = await InDb(env.App, db => db.Repositories.Where(r => r.Id == active).Select(r => r.IndexId).SingleAsync());

        var response = await env.Client.PostAsync($"/api/conversations/{id}/messages", new { question = "where?" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(indexOfActive, Assert.Single(env.Rag.Queries).IndexId);
        Assert.Empty(env.Rag.MultiQueries);
    }

    [Fact]
    public async Task New_Message_Columns_Stay_Empty_Until_The_Multi_Flow_Writes_Them()
    {
        var env = await NewEnvAsync();
        var repo = await ImportAsync(env);
        var id = await CreateConversationAsync(env, repo);
        await env.Client.PostAsync($"/api/conversations/{id}/messages", new { question = "where?" });

        var messages = await InDb(env.App, db => db.Messages.AsNoTracking().Where(m => m.ConversationId == id).ToListAsync());

        Assert.Equal(2, messages.Count);
        Assert.All(messages, m =>
        {
            Assert.Null(m.RetrievalSnapshot);
            Assert.Null(m.FocusRepositoryIds);
        });
    }

    // =========================================================================================================
    // no index id, no user content in logs
    // =========================================================================================================

    [Fact]
    public async Task No_Scope_Response_Not_Even_An_Error_Contains_An_Index_Id()
    {
        var env = await NewEnvAsync(("CONVERSATION_SCOPE_MAX", "2"));
        var a = await ImportAsync(env);
        var b = await ImportAsync(env);
        var c = await ImportAsync(env);
        var pending = await ImportAsync(env, ready: false);
        var indexIds = await InDb(env.App, db => db.Repositories.Select(r => r.IndexId!).Where(i => i != null).ToListAsync());
        Assert.NotEmpty(indexIds);

        var bodies = new List<string>();
        async Task Capture(Task<HttpResponseMessage> call) => bodies.Add(await (await call).Content.ReadAsStringAsync());
        await Capture(env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", new { title = "x", initialRepositoryId = a }));
        await Capture(env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", new { title = "x", initialRepositoryId = pending }));       // 409
        await Capture(env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", new { title = "x", initialRepositoryId = Guid.NewGuid() })); // 404
        var id = await CreateConversationAsync(env, a);
        await Capture(env.Client.GetAsync($"/api/conversations/{id}/repositories"));
        await Capture(env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = b }));
        await Capture(env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = b }));       // idempotent
        await Capture(env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = c }));       // 409 cap
        await Capture(env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = pending })); // 409 not ready
        await Capture(env.Client.PostAsync($"/api/conversations/{id}/repositories", new { }));                        // 400
        await Capture(env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{Guid.NewGuid()}"));             // 404
        await Capture(env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{a}"));                          // 204
        await Capture(env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{b}"));                          // 409 last

        Assert.Equal(12, bodies.Count);
        Assert.All(bodies, body =>
        {
            Assert.DoesNotContain("indexid", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("index_id", body, StringComparison.OrdinalIgnoreCase);
            foreach (var index in indexIds) Assert.DoesNotContain(index, body);
        });
    }

    [Fact]
    public async Task Title_And_Repository_Url_Never_Reach_The_Logs()
    {
        var env = await NewEnvAsync(("CONVERSATION_SCOPE_MAX", "2"));
        var tag = Guid.NewGuid().ToString("N")[..12];
        var url = $"https://github.com/leak-{tag}/repo-{tag}";
        var imported = await env.Client.PostAsync($"/api/projects/{env.ProjectId}/repositories", new { repoUrl = url });
        var repo = (await imported.Content.ReadFromJsonAsync<RepositoryDto>())!.Id;
        env.Rag.OnGet = RecordingRagClient.Ready;
        await env.Client.GetAsync($"/api/repositories/{repo}");
        env.Rag.OnGet = null;
        var other = await ImportAsync(env);
        var third = await ImportAsync(env);

        var created = await env.Client.PostAsync($"/api/projects/{env.ProjectId}/conversations", new { title = $"title-{tag}", initialRepositoryId = repo });
        var id = (await created.Content.ReadFromJsonAsync<ConversationDto>())!.Id;
        await env.Client.GetAsync($"/api/conversations/{id}/repositories");
        await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = other });
        await env.Client.PostAsync($"/api/conversations/{id}/repositories", new { repositoryId = third }); // 409 cap
        await env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{other}");
        await env.Client.DeleteAsync($"/api/conversations/{id}/repositories/{repo}"); // 409 last
        await env.Client.PostAsync($"/api/conversations/{id}/messages", new { question = "where?" });

        var entries = factory.Logs.Entries;
        Assert.Contains(entries, l => l.Contains("Answered conversation")); // the capture works and the journey was logged
        Assert.DoesNotContain(entries, l => l.Contains(tag, StringComparison.Ordinal));
    }
}
