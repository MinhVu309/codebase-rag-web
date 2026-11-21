using System.Net;
using System.Net.Http.Json;
using Api.Tests.Projects;
using Application.Projects;
using Application.Repositories;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Api.Tests.Security.SecurityTestSupport;

namespace Api.Tests.Security;

/// <summary>
/// Every endpoint that takes an id of a project, repository or conversation must answer someone else's id exactly
/// like a missing one (404), and must never expose RAG's index_id. The matrix is checked against the real route
/// table, so adding an endpoint without adding it here fails the build.
/// </summary>
[Collection(ApiCollection.Name)]
public class OwnershipMatrixTests(ApiFactory factory)
{
    private sealed record Ids(Guid Project, Guid Repository, Guid Conversation);

    private sealed record Entry(string Method, string Pattern, Func<Ids, (string Url, object? Body)> Build)
    {
        public override string ToString() => $"{Method} {Pattern}";
    }

    private static readonly Entry[] Matrix =
    [
        new("GET", "api/projects/{id:guid}", i => ($"/api/projects/{i.Project}", null)),
        new("PUT", "api/projects/{id:guid}", i => ($"/api/projects/{i.Project}", new { name = "renamed" })),
        new("DELETE", "api/projects/{id:guid}", i => ($"/api/projects/{i.Project}", null)),
        new("POST", "api/projects/{projectId:guid}/repositories", i => ($"/api/projects/{i.Project}/repositories", new { repoUrl = "https://github.com/octo/other" })),
        new("GET", "api/projects/{projectId:guid}/conversations", i => ($"/api/projects/{i.Project}/conversations", null)),
        new("POST", "api/projects/{projectId:guid}/conversations", i => ($"/api/projects/{i.Project}/conversations", new { title = "x" })),
        new("GET", "api/repositories/{id:guid}", i => ($"/api/repositories/{i.Repository}", null)),
        new("GET", "api/repositories/{id:guid}/files", i => ($"/api/repositories/{i.Repository}/files?path=src/a.cs", null)),
        new("DELETE", "api/repositories/{id:guid}", i => ($"/api/repositories/{i.Repository}", null)),
        new("PATCH", "api/conversations/{id:guid}", i => ($"/api/conversations/{i.Conversation}", new { title = "renamed" })),
        new("DELETE", "api/conversations/{id:guid}", i => ($"/api/conversations/{i.Conversation}", null)),
        new("GET", "api/conversations/{id:guid}/messages", i => ($"/api/conversations/{i.Conversation}/messages", null)),
        new("POST", "api/conversations/{id:guid}/messages", i => ($"/api/conversations/{i.Conversation}/messages", new { question = "hi" })),
    ];

    public static IEnumerable<object[]> Cases() => Matrix.Select((_, i) => new object[] { i });

    private static Task<HttpResponseMessage> Send(AuthTestClient client, Entry e, Ids ids)
    {
        var (url, body) = e.Build(ids);
        var request = new HttpRequestMessage(new HttpMethod(e.Method), url);
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    [Fact]
    public void Every_Route_With_An_Id_Is_In_The_Matrix()
    {
        var routes = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.Parameters.Any(p => p.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => $"{m} {e.RoutePattern.RawText}"))
            .Order().ToList();

        Assert.Equal(Matrix.Select(m => m.ToString()).Order().ToList(), routes);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Someone_Elses_Id_Is_404_Anonymous_Is_401_And_The_Owner_Is_Not_404(int index)
    {
        var entry = Matrix[index];
        var rag = new RecordingRagClient();
        using var app = factory.With(rag);
        var owner = await NewUserWithConversationAsync(app, rag);
        var ids = new Ids(owner.ProjectId, owner.RepositoryId, owner.ConversationId);
        var stranger = await AuthTestClient.NewUserAsync(app);

        var asStranger = await Send(stranger, entry, ids);
        Assert.True(HttpStatusCode.NotFound == asStranger.StatusCode, $"{entry}: stranger got {(int)asStranger.StatusCode}");

        // exactly the answer a nonexistent id gets, so ids cannot be probed
        var missing = await Send(stranger, entry, new Ids(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await Normalize(missing), await Normalize(asStranger));

        var anonymous = await Send(new AuthTestClient(app), entry, ids);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // the stranger changed nothing
        var project = await owner.Client.GetAsync($"/api/projects/{owner.ProjectId}");
        Assert.Equal(HttpStatusCode.OK, project.StatusCode);
        var detail = (await project.Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        Assert.Equal("p", detail.Name);
        Assert.Single(detail.Repositories);
        Assert.Empty(rag.Deleted);

        // and the endpoint itself exists for the owner
        var asOwner = await Send(owner.Client, entry, ids);
        Assert.True(HttpStatusCode.NotFound != asOwner.StatusCode, $"{entry}: owner got 404");
    }

    /// <summary>The 404 body of a foreign id and a missing id must not differ except for the per-request trace id.</summary>
    private static async Task<string> Normalize(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return System.Text.RegularExpressions.Regex.Replace(json, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"-\"");
    }

    [Fact]
    public async Task The_Index_Id_Never_Appears_In_Any_Response()
    {
        var rag = new RecordingRagClient();
        using var app = factory.With(rag);
        var s = await NewUserWithConversationAsync(app, rag);
        var bodies = new List<string>();
        async Task Capture(Task<HttpResponseMessage> call) => bodies.Add(await (await call).Content.ReadAsStringAsync());

        await Capture(s.Client.GetAsync("/api/projects"));
        await Capture(s.Client.GetAsync($"/api/projects/{s.ProjectId}"));
        await Capture(s.Client.GetAsync($"/api/repositories/{s.RepositoryId}"));
        await Capture(s.Client.GetAsync($"/api/repositories/{s.RepositoryId}/files?path=src/a.cs&start=1&end=3"));
        await Capture(s.Client.GetAsync($"/api/projects/{s.ProjectId}/conversations"));
        await Capture(s.Client.PostAsync($"/api/conversations/{s.ConversationId}/messages", new { question = "where?" }));
        await Capture(s.Client.GetAsync($"/api/conversations/{s.ConversationId}/messages"));
        await Capture(s.Client.PostAsync($"/api/projects/{s.ProjectId}/repositories", new { repoUrl = "https://github.com/octo/second" }));
        await Capture(s.Client.GetAsync("/api/auth/me"));
        rag.QueryError = RecordingRagClient.Error(Application.Rag.RagFailureKind.LlmError, "LLM_ERROR");
        await Capture(s.Client.PostAsync($"/api/conversations/{s.ConversationId}/messages", new { question = "again" })); // an error body too

        var indexId = Assert.Single(rag.Queries.Select(q => q.IndexId).Distinct());
        Assert.NotEmpty(rag.FileCalls);
        Assert.All(bodies, body =>
        {
            Assert.DoesNotContain(indexId, body);
            Assert.DoesNotContain("indexid", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("index_id", body, StringComparison.OrdinalIgnoreCase);
        });
    }
}
