using System.Net;
using System.Net.Http.Json;
using Api.Tests.Projects;
using Application.Rag;
using static Api.Tests.Security.SecurityTestSupport;

namespace Api.Tests.Security;

/// <summary>Rate limits are switched off in ApiFactory; every test here turns on a small one for its own host.</summary>
[Collection(ApiCollection.Name)]
public class RateLimitTests(ApiFactory factory)
{
    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemBody>())!;
        Assert.Equal(429, problem.Status);
        Assert.Equal("RATE_LIMITED", problem.Code);

        Assert.True(response.Headers.TryGetValues("Retry-After", out var values));
        Assert.True(int.TryParse(values!.Single(), out var seconds), "Retry-After is whole seconds");
        Assert.InRange(seconds, 1, 600);
    }

    // ---------- auth: per IP ----------

    [Fact]
    public async Task Login_Register_And_Refresh_Share_A_Per_IP_Limit_And_Then_Get_429()
    {
        using var app = factory.With(null, ("RATE_LIMIT_AUTH_PER_MINUTE", "3"));
        var client = new AuthTestClient(app);
        var bad = new { email = AuthTestClient.NewEmail(), password = "wrong-password-1" };

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/login", bad)).StatusCode);

        await AssertRateLimitedAsync(await client.PostAsync("/api/auth/login", bad));
        await AssertRateLimitedAsync(await client.PostAsync("/api/auth/refresh"));
        await AssertRateLimitedAsync(await client.PostAsync("/api/auth/register", new { email = AuthTestClient.NewEmail(), password = "S3cret-Passw0rd!" }));
    }

    [Fact]
    public async Task Logout_Me_And_Health_Are_Not_Counted_Against_The_Auth_Limit()
    {
        using var app = factory.With(null, ("RATE_LIMIT_AUTH_PER_MINUTE", "1"));
        var client = new AuthTestClient(app);
        await client.PostAsync("/api/auth/login", new { email = AuthTestClient.NewEmail(), password = "wrong-password-1" });

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/auth/login", new { email = "a@b.co", password = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    // ---------- forwarded headers: the IP the limiter sees ----------

    private static HttpRequestMessage Login(string? forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "nobody@example.com", password = "wrong-password-1" }),
        };
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    [Fact]
    public async Task Behind_A_Trusted_Proxy_The_Limit_Follows_X_Forwarded_For()
    {
        using var app = factory.WithRemoteIp("10.1.2.3", ("RATE_LIMIT_AUTH_PER_MINUTE", "2"), ("TRUSTED_PROXIES", "10.1.2.0/24"));
        var http = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Login("203.0.113.10"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Login("203.0.113.10"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.SendAsync(Login("203.0.113.10"))).StatusCode);
        // a different real client behind the same proxy has its own budget
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Login("203.0.113.11"))).StatusCode);
    }

    [Fact]
    public async Task Without_TRUSTED_PROXIES_X_Forwarded_For_Is_Ignored_So_It_Cannot_Be_Used_To_Dodge_The_Limit()
    {
        using var app = factory.WithRemoteIp("10.1.2.3", ("RATE_LIMIT_AUTH_PER_MINUTE", "2"));
        var http = app.CreateClient();

        await http.SendAsync(Login("203.0.113.20"));
        await http.SendAsync(Login("203.0.113.21"));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.SendAsync(Login("203.0.113.22"))).StatusCode);
    }

    [Fact]
    public async Task X_Forwarded_For_From_An_Address_That_Is_Not_A_Trusted_Proxy_Is_Ignored()
    {
        using var app = factory.WithRemoteIp("192.0.2.50", ("RATE_LIMIT_AUTH_PER_MINUTE", "2"), ("TRUSTED_PROXIES", "10.1.2.0/24"));
        var http = app.CreateClient();

        await http.SendAsync(Login("203.0.113.30"));
        await http.SendAsync(Login("203.0.113.31"));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.SendAsync(Login("203.0.113.32"))).StatusCode);
    }

    // ---------- ask: per user ----------

    [Fact]
    public async Task Ask_Is_Limited_Per_User_And_A_Blocked_Request_Never_Reaches_RAG()
    {
        var rag = new RecordingRagClient();
        using var app = factory.With(rag, ("RATE_LIMIT_ASK_PER_MINUTE", "2"));
        var a = await NewUserWithConversationAsync(app, rag);
        var b = await NewUserWithConversationAsync(app, rag);
        string Url(Setup s) => $"/api/conversations/{s.ConversationId}/messages";

        Assert.Equal(HttpStatusCode.Created, (await a.Client.PostAsync(Url(a), new { question = "one" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await a.Client.PostAsync(Url(a), new { question = "two" })).StatusCode);
        await AssertRateLimitedAsync(await a.Client.PostAsync(Url(a), new { question = "three" }));
        Assert.Equal(2, rag.Queries.Count);

        // B is a different user on the same IP: unaffected.
        Assert.Equal(HttpStatusCode.Created, (await b.Client.PostAsync(Url(b), new { question = "hello" })).StatusCode);
        // Reading history is not "asking".
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync(Url(a))).StatusCode);
    }

    // ---------- import: per user, plus one running import at a time ----------

    [Fact]
    public async Task Import_Is_Limited_Per_User_Over_A_Window()
    {
        var rag = new RecordingRagClient();
        using var app = factory.With(rag, ("RATE_LIMIT_IMPORT_PER_WINDOW", "2"));
        var client = await AuthTestClient.NewUserAsync(app);
        var project = (await (await client.PostAsync("/api/projects", new { name = "p" })).Content.ReadFromJsonAsync<Application.Projects.ProjectDetailDto>())!;
        string Url() => $"/api/projects/{project.Id}/repositories";

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(Url(), new { repoUrl = "https://github.com/octo/one" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(Url(), new { repoUrl = "https://github.com/octo/two" })).StatusCode);
        await AssertRateLimitedAsync(await client.PostAsync(Url(), new { repoUrl = "https://github.com/octo/three" }));
        Assert.Equal(2, rag.Created.Count);
    }

    [Fact]
    public async Task A_Second_Import_While_One_Is_Still_Indexing_Is_409_IMPORT_IN_PROGRESS_Until_It_Finishes()
    {
        var rag = new RecordingRagClient(); // default status: still embedding
        using var app = factory.With(rag, ("MAX_CONCURRENT_IMPORTS_PER_USER", "1"));
        var client = await AuthTestClient.NewUserAsync(app);
        var other = await AuthTestClient.NewUserAsync(app);
        var project = (await (await client.PostAsync("/api/projects", new { name = "p" })).Content.ReadFromJsonAsync<Application.Projects.ProjectDetailDto>())!;
        var otherProject = (await (await other.PostAsync("/api/projects", new { name = "q" })).Content.ReadFromJsonAsync<Application.Projects.ProjectDetailDto>())!;
        string Url(Guid id) => $"/api/projects/{id}/repositories";

        var first = await client.PostAsync(Url(project.Id), new { repoUrl = "https://github.com/octo/one" });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var blocked = await client.PostAsync(Url(project.Id), new { repoUrl = "https://github.com/octo/two" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("IMPORT_IN_PROGRESS", (await blocked.Content.ReadFromJsonAsync<ProblemBody>())!.Code);
        Assert.Single(rag.Created); // the blocked one never reached RAG

        // the cap is per user, and another project of the same user counts too
        Assert.Equal(HttpStatusCode.Accepted, (await other.PostAsync(Url(otherProject.Id), new { repoUrl = "https://github.com/octo/two" })).StatusCode);
        var otherProjectOfSameUser = (await (await client.PostAsync("/api/projects", new { name = "p2" })).Content.ReadFromJsonAsync<Application.Projects.ProjectDetailDto>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(Url(otherProjectOfSameUser.Id), new { repoUrl = "https://github.com/octo/three" })).StatusCode);

        // The first job finishes in RAG; nobody polled, yet the next import must go through (statuses are refreshed first).
        rag.OnGet = RecordingRagClient.Ready;
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(Url(project.Id), new { repoUrl = "https://github.com/octo/two" })).StatusCode);
    }

    [Fact]
    public async Task A_Failed_Import_Does_Not_Hold_The_Slot()
    {
        var rag = new RecordingRagClient { CreateError = RecordingRagClient.Error(RagFailureKind.Unavailable, RagErrorCodes.InternalError) };
        using var app = factory.With(rag, ("MAX_CONCURRENT_IMPORTS_PER_USER", "1"));
        var client = await AuthTestClient.NewUserAsync(app);
        var project = (await (await client.PostAsync("/api/projects", new { name = "p" })).Content.ReadFromJsonAsync<Application.Projects.ProjectDetailDto>())!;
        string Url() => $"/api/projects/{project.Id}/repositories";

        Assert.False((await client.PostAsync(Url(), new { repoUrl = "https://github.com/octo/one" })).IsSuccessStatusCode); // RAG down: row is kept as failed
        rag.CreateError = null;

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(Url(), new { repoUrl = "https://github.com/octo/two" })).StatusCode);
    }
}
