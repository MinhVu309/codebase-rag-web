using System.Net;
using System.Net.Http.Json;
using Application.Projects;
using Application.Rag;
using Application.Repositories;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Projects;

[Collection(ApiCollection.Name)]
public class RepositoryEndpointTests(ApiFactory factory)
{
    private const string Url = "https://github.com/octo/hello";

    private record ProblemWithCode(int Status, string? Title, string? Detail, string? Code);

    /// <summary>A copy of the app whose RAG client is the given test double.</summary>
    private WebApplicationFactory<Program> WithRag(IRagClient rag) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(rag)));

    private static async Task<ProjectDetailDto> CreateProjectAsync(AuthTestClient client)
    {
        var response = await client.PostAsync("/api/projects", new { name = "p" });
        return (await response.Content.ReadFromJsonAsync<ProjectDetailDto>())!;
    }

    private static async Task<RepositoryDto> ImportAsync(AuthTestClient client, Guid projectId, string url = Url, string? gitRef = null)
    {
        var response = await client.PostAsync($"/api/projects/{projectId}/repositories", new { repoUrl = url, @ref = gitRef });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RepositoryDto>())!;
    }

    private static async Task<RepositoryDto> GetRepoAsync(AuthTestClient client, Guid id) =>
        (await (await client.GetAsync($"/api/repositories/{id}")).Content.ReadFromJsonAsync<RepositoryDto>())!;

    private static async Task<ProjectDetailDto> GetProjectAsync(AuthTestClient client, Guid id) =>
        (await (await client.GetAsync($"/api/projects/{id}")).Content.ReadFromJsonAsync<ProjectDetailDto>())!;

    // ---------- import ----------

    [Fact]
    public async Task Import_Returns_202_Pending_And_Sends_Normalized_Request_To_RAG()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsync($"/api/projects/{project.Id}/repositories",
            new { repoUrl = "  https://github.com/octo/hello.git/  " });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var repo = (await response.Content.ReadFromJsonAsync<RepositoryDto>())!;
        Assert.Equal("pending", repo.Status);
        Assert.Equal(Url, repo.RepoUrl);
        Assert.Equal("main", repo.Ref);
        Assert.True(repo.IsActive);

        var call = Assert.Single(rag.Created);
        Assert.Equal(Url, call.Url);
        Assert.Equal("main", call.Ref);
        Assert.Equal(repo.Id.ToString(), call.ClientRef); // our repository id is RAG's client_ref (D12)
    }

    [Fact]
    public async Task Import_Never_Exposes_The_RAG_Index_Id_To_The_Frontend()
    {
        using var app = WithRag(new RecordingRagClient());
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        var response = await client.PostAsync($"/api/projects/{project.Id}/repositories", new { repoUrl = Url });

        var body = await response.Content.ReadAsStringAsync();
        var projectBody = await (await client.GetAsync($"/api/projects/{project.Id}")).Content.ReadAsStringAsync();

        foreach (var text in new[] { body, projectBody })
        {
            Assert.DoesNotContain("indexId", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("index_id", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("idx-", text);
        }
    }

    [Fact]
    public async Task Import_Passes_A_Custom_Ref_Through()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);

        var repo = await ImportAsync(client, project.Id, Url, "release/1.2");

        Assert.Equal("release/1.2", repo.Ref);
        Assert.Equal("release/1.2", rag.Created.Single().Ref);
    }

    [Theory]
    [InlineData("")]
    [InlineData("github.com/octo/hello")]                     // no scheme
    [InlineData("http://github.com/octo/hello")]              // not https
    [InlineData("https://gitlab.com/octo/hello")]             // other host
    [InlineData("https://github.com.evil.com/octo/hello")]    // look-alike host
    [InlineData("https://evil.com/https://github.com/o/r")]
    [InlineData("https://user:pw@github.com/octo/hello")]     // credentials
    [InlineData("https://github.com:8443/octo/hello")]        // port
    [InlineData("https://github.com/octo")]                   // no repo
    [InlineData("https://github.com/octo/hello/tree/main")]   // sub-path
    [InlineData("https://github.com/octo/hello?x=1")]         // query
    [InlineData("https://github.com/octo/hello#readme")]      // fragment
    [InlineData("https://github.com/octo/..")]                // traversal
    [InlineData("https://github.com/-octo/hello")]            // invalid owner
    [InlineData("git@github.com:octo/hello.git")]             // ssh form
    [InlineData("file:///etc/passwd")]
    [InlineData("https://github.com/octo/hello world")]
    public async Task Import_Rejects_Anything_That_Is_Not_A_Plain_GitHub_Repo_Url(string url)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsync($"/api/projects/{project.Id}/repositories", new { repoUrl = url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(rag.Created);
        Assert.Empty((await GetProjectAsync(client, project.Id)).Repositories);
    }

    [Theory]
    [InlineData("--upload-pack=evil")]   // would be read as a git option
    [InlineData("-x")]
    [InlineData("a..b")]
    [InlineData("a b")]
    [InlineData("a;rm -rf /")]
    [InlineData("branch/")]
    [InlineData("")]
    public async Task Import_Rejects_Dangerous_Or_Malformed_Refs(string gitRef)
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsync($"/api/projects/{project.Id}/repositories", new { repoUrl = Url, @ref = gitRef });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(rag.Created);
    }

    [Fact]
    public async Task Importing_The_Same_Repo_And_Ref_Twice_Conflicts_But_A_Different_Ref_Is_Fine()
    {
        using var app = WithRag(new RecordingRagClient());
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        await ImportAsync(client, project.Id);

        var again = await client.PostAsync($"/api/projects/{project.Id}/repositories", new { repoUrl = Url });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        await ImportAsync(client, project.Id, Url, "develop");
    }

    [Fact]
    public async Task A_Failed_Import_Can_Be_Retried()
    {
        var rag = new RecordingRagClient { CreateError = RecordingRagClient.Error(RagFailureKind.Timeout, "TIMEOUT") };
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        var first = await client.PostAsync($"/api/projects/{project.Id}/repositories", new { repoUrl = Url });
        Assert.Equal(HttpStatusCode.GatewayTimeout, first.StatusCode);

        rag.CreateError = null;
        var retry = await ImportAsync(client, project.Id);

        Assert.Equal("pending", retry.Status);
    }

    // ---------- synchronous RAG failures ----------

    [Theory]
    [InlineData(RagFailureKind.Invalid, "INVALID_REPO", 422)]
    [InlineData(RagFailureKind.Timeout, "TIMEOUT", 504)]
    [InlineData(RagFailureKind.Unavailable, "RAG_UNAVAILABLE", 502)]
    [InlineData(RagFailureKind.Unauthorized, "UNAUTHORIZED", 502)]
    public async Task Import_Surfaces_Sync_RAG_Errors_And_Keeps_A_Failed_Row(RagFailureKind kind, string code, int expectedStatus)
    {
        var rag = new RecordingRagClient { CreateError = RecordingRagClient.Error(kind, code) };
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsync($"/api/projects/{project.Id}/repositories", new { repoUrl = Url });

        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
        var repo = Assert.Single((await GetProjectAsync(client, project.Id)).Repositories);
        Assert.Equal("failed", repo.Status);
        Assert.Equal(code, repo.ErrorCode);
        Assert.False(repo.IsActive);
    }

    // ---------- status / polling ----------

    [Fact]
    public async Task Polling_Walks_The_Index_To_Ready_Using_The_Fake_RAG_End_To_End()
    {
        // The default test app runs RAG_MODE=fake: this is the whole import flow with no test double.
        var client = await AuthTestClient.NewUserAsync(factory);
        var project = await CreateProjectAsync(client);
        var repo = await ImportAsync(client, project.Id);
        Assert.Equal("pending", repo.Status);

        var seen = new List<string>();
        RepositoryDto current;
        do
        {
            current = await GetRepoAsync(client, repo.Id);
            seen.Add(current.Status);
        } while (current.Status != "ready" && seen.Count < 20);

        Assert.Equal(["cloning", "parsing", "embedding", "ready"], seen);
        Assert.NotNull(current.CommitSha);
        Assert.Equal(12, current.Files);
        Assert.Equal(48, current.Chunks);
        Assert.False(current.Stale);
        Assert.True(current.IsActive);
        Assert.Equal("ready", (await GetProjectAsync(client, project.Id)).Repositories.Single().Status);
    }

    [Fact]
    public async Task Status_Maps_Progress_And_Stops_Calling_RAG_Once_Ready()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        var repo = await ImportAsync(client, project.Id);

        var building = await GetRepoAsync(client, repo.Id);
        Assert.Equal("embedding", building.Status);
        Assert.Equal(3, building.FilesDone);
        Assert.Equal(10, building.TotalFiles);

        rag.OnGet = RecordingRagClient.Ready;
        var ready = await GetRepoAsync(client, repo.Id);
        Assert.Equal("ready", ready.Status);
        Assert.Equal("abc123", ready.CommitSha);
        var callsWhenReady = rag.GetCalls;

        await GetRepoAsync(client, repo.Id);
        await GetRepoAsync(client, repo.Id);

        Assert.Equal(callsWhenReady, rag.GetCalls); // terminal state is served from the database
    }

    [Fact]
    public async Task A_Failed_Index_Surfaces_The_RAG_Error_Code_And_Stops_Polling()
    {
        var rag = new RecordingRagClient
        {
            OnGet = id => new RagIndexStatus(id, IndexStatus.Failed, "failed", 0, 0, null, null, null,
                new RagIndexError("REPO_TOO_LARGE", "too big")),
        };
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        var repo = await ImportAsync(client, project.Id);

        var failed = await GetRepoAsync(client, repo.Id);
        await GetRepoAsync(client, repo.Id);

        Assert.Equal("failed", failed.Status);
        Assert.Equal("REPO_TOO_LARGE", failed.ErrorCode);
        Assert.Equal(1, rag.GetCalls);
    }

    [Fact]
    public async Task A_Transient_RAG_Failure_While_Polling_Returns_The_Last_Known_State_Marked_Stale()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        var repo = await ImportAsync(client, project.Id);
        Assert.Equal("embedding", (await GetRepoAsync(client, repo.Id)).Status);

        rag.GetError = RecordingRagClient.Error(RagFailureKind.Unavailable, "RAG_UNAVAILABLE");
        var response = await client.GetAsync($"/api/repositories/{repo.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stale = (await response.Content.ReadFromJsonAsync<RepositoryDto>())!;
        Assert.Equal("embedding", stale.Status);
        Assert.True(stale.Stale);

        rag.GetError = null;
        Assert.False((await GetRepoAsync(client, repo.Id)).Stale);
    }

    [Fact]
    public async Task If_RAG_Forgot_The_Index_The_Repository_Becomes_Failed()
    {
        var rag = new RecordingRagClient { GetError = RecordingRagClient.Error(RagFailureKind.NotFound, "INDEX_NOT_FOUND") };
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        var repo = await ImportAsync(client, project.Id);

        var result = await GetRepoAsync(client, repo.Id);

        Assert.Equal("failed", result.Status);
        Assert.Equal("INDEX_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task An_Import_That_Never_Got_An_Index_Id_Is_Eventually_Marked_Interrupted()
    {
        using var app = WithRag(new RecordingRagClient());
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        Guid repoId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            repoId = Guid.NewGuid();
            db.Repositories.Add(new CodeRepository
            {
                Id = repoId, ProjectId = project.Id, RepoUrl = Url, Ref = "main", Status = IndexStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10), UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            });
            await db.SaveChangesAsync();
        }

        var result = await GetRepoAsync(client, repoId);

        Assert.Equal("failed", result.Status);
        Assert.Equal("IMPORT_INTERRUPTED", result.ErrorCode);
    }

    // ---------- delete repository ----------

    [Fact]
    public async Task Delete_Repository_Deletes_The_RAG_Index_And_Then_The_Row()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        var repo = await ImportAsync(client, project.Id);

        var response = await client.DeleteAsync($"/api/repositories/{repo.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(rag.Deleted);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/repositories/{repo.Id}")).StatusCode);
        Assert.Empty((await GetProjectAsync(client, project.Id)).Repositories);
    }

    [Fact]
    public async Task Delete_Repository_Keeps_The_Row_When_RAG_Fails_So_Retry_Is_Safe()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        var repo = await ImportAsync(client, project.Id);

        rag.DeleteError = RecordingRagClient.Error(RagFailureKind.Timeout, "TIMEOUT");
        var failed = await client.DeleteAsync($"/api/repositories/{repo.Id}");
        Assert.Equal(HttpStatusCode.GatewayTimeout, failed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/repositories/{repo.Id}")).StatusCode);

        rag.DeleteError = null;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/repositories/{repo.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_The_Active_Repository_Falls_Back_To_The_Previous_One()
    {
        using var app = WithRag(new RecordingRagClient());
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        var older = await ImportAsync(client, project.Id, Url, "main");
        var newer = await ImportAsync(client, project.Id, Url, "develop");
        var before = (await GetProjectAsync(client, project.Id)).Repositories;
        Assert.True(before.Single(r => r.Id == newer.Id).IsActive);
        Assert.False(before.Single(r => r.Id == older.Id).IsActive);

        await client.DeleteAsync($"/api/repositories/{newer.Id}");

        var after = (await GetProjectAsync(client, project.Id)).Repositories;
        Assert.True(Assert.Single(after).IsActive);
        Assert.Equal(older.Id, after.Single().Id);

        await client.DeleteAsync($"/api/repositories/{older.Id}");
        Assert.Empty((await GetProjectAsync(client, project.Id)).Repositories);
    }

    // ---------- delete project ----------

    [Fact]
    public async Task Delete_Project_Removes_Every_Index_In_RAG()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        await ImportAsync(client, project.Id, Url, "main");
        await ImportAsync(client, project.Id, Url, "develop");

        var response = await client.DeleteAsync($"/api/projects/{project.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, rag.Deleted.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_Project_Is_Kept_When_RAG_Fails()
    {
        var rag = new RecordingRagClient();
        using var app = WithRag(rag);
        var client = await AuthTestClient.NewUserAsync(app);
        var project = await CreateProjectAsync(client);
        await ImportAsync(client, project.Id);
        rag.DeleteError = RecordingRagClient.Error(RagFailureKind.Unavailable, "RAG_UNAVAILABLE");

        var response = await client.DeleteAsync($"/api/projects/{project.Id}");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Single((await GetProjectAsync(client, project.Id)).Repositories);
    }
}
