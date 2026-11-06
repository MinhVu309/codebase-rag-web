using System.Net;
using System.Net.Http.Json;
using Application.Projects;
using Application.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Api.Tests.Projects;

[Collection(ApiCollection.Name)]
public class ProjectEndpointTests(ApiFactory factory)
{
    private async Task<ProjectDetailDto> CreateProjectAsync(AuthTestClient client, string name = "My project")
    {
        var response = await client.PostAsync("/api/projects", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectDetailDto>())!;
    }

    // ---------- authentication ----------

    [Theory]
    [InlineData("GET", "/api/projects")]
    [InlineData("POST", "/api/projects")]
    [InlineData("GET", "/api/projects/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/repositories/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/repositories/00000000-0000-0000-0000-000000000001")]
    public async Task Endpoints_Require_Authentication(string method, string url)
    {
        var client = factory.CreateAuthClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- CRUD ----------

    [Fact]
    public async Task Create_List_Get_Rename_Delete_Roundtrip()
    {
        var client = await AuthTestClient.NewUserAsync(factory);

        var created = await CreateProjectAsync(client, "  Backend  ");
        Assert.Equal("Backend", created.Name); // trimmed

        var list = await (await client.GetAsync("/api/projects")).Content.ReadFromJsonAsync<List<ProjectSummaryDto>>();
        Assert.Equal(created.Id, Assert.Single(list!).Id);

        var renamed = await client.PutAsync($"/api/projects/{created.Id}", new { name = "Renamed" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var detail = await (await client.GetAsync($"/api/projects/{created.Id}")).Content.ReadFromJsonAsync<ProjectDetailDto>();
        Assert.Equal("Renamed", detail!.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/projects/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task List_Returns_Newest_First()
    {
        var client = await AuthTestClient.NewUserAsync(factory);
        var first = await CreateProjectAsync(client, "first");
        var second = await CreateProjectAsync(client, "second");

        var list = await (await client.GetAsync("/api/projects")).Content.ReadFromJsonAsync<List<ProjectSummaryDto>>();

        Assert.Equal([second.Id, first.Id], list!.Select(p => p.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_Rejects_Blank_Names(string name)
    {
        var client = await AuthTestClient.NewUserAsync(factory);

        var response = await client.PostAsync("/api/projects", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors);
    }

    [Fact]
    public async Task Create_And_Rename_Reject_Overlong_Names()
    {
        var client = await AuthTestClient.NewUserAsync(factory);
        var tooLong = new string('x', ProjectRules.MaxNameLength + 1);
        var project = await CreateProjectAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/projects", new { name = tooLong })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync($"/api/projects/{project.Id}", new { name = tooLong })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsync("/api/projects", new { name = new string('x', ProjectRules.MaxNameLength) })).StatusCode);
    }

    [Fact]
    public async Task Missing_Body_Fields_Are_Validation_Errors_Not_Server_Errors()
    {
        var client = await AuthTestClient.NewUserAsync(factory);

        var response = await client.PostAsync("/api/projects", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- tenant isolation: user A can never reach user B's data ----------

    [Fact]
    public async Task User_B_Cannot_Read_Modify_Or_Delete_User_As_Project()
    {
        var a = await AuthTestClient.NewUserAsync(factory);
        var b = await AuthTestClient.NewUserAsync(factory);
        var project = await CreateProjectAsync(a, "A's secret project");
        var url = $"/api/projects/{project.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsync(url, new { name = "hijacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await b.PostAsync($"{url}/repositories", new { repoUrl = "https://github.com/o/r" })).StatusCode);

        // B's own list does not leak A's project ...
        var bList = await (await b.GetAsync("/api/projects")).Content.ReadFromJsonAsync<List<ProjectSummaryDto>>();
        Assert.Empty(bList!);

        // ... and A's project is untouched by all of the above.
        var after = await (await a.GetAsync(url)).Content.ReadFromJsonAsync<ProjectDetailDto>();
        Assert.Equal("A's secret project", after!.Name);
        Assert.Empty(after.Repositories);
    }

    [Fact]
    public async Task Someone_Elses_Project_Looks_Exactly_Like_A_Missing_One()
    {
        var a = await AuthTestClient.NewUserAsync(factory);
        var b = await AuthTestClient.NewUserAsync(factory);
        var project = await CreateProjectAsync(a);

        var other = await b.GetAsync($"/api/projects/{project.Id}");
        var missing = await b.GetAsync($"/api/projects/{Guid.NewGuid()}");

        Assert.Equal(missing.StatusCode, other.StatusCode);
        var otherBody = await other.Content.ReadFromJsonAsync<ProblemDetails>();
        var missingBody = await missing.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(missingBody!.Title, otherBody!.Title);
        Assert.Equal(missingBody.Detail, otherBody.Detail);
    }

    [Fact]
    public async Task User_B_Cannot_Read_Or_Delete_User_As_Repository()
    {
        var a = await AuthTestClient.NewUserAsync(factory);
        var b = await AuthTestClient.NewUserAsync(factory);
        var project = await CreateProjectAsync(a);
        var import = await a.PostAsync($"/api/projects/{project.Id}/repositories", new { repoUrl = "https://github.com/o/r" });
        Assert.Equal(HttpStatusCode.Accepted, import.StatusCode);
        var repo = (await import.Content.ReadFromJsonAsync<RepositoryDto>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/repositories/{repo.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/repositories/{repo.Id}")).StatusCode);

        // A still has it.
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/repositories/{repo.Id}")).StatusCode);
    }

    [Fact]
    public async Task Different_Users_Can_Use_The_Same_Project_Name_And_Repository()
    {
        var a = await AuthTestClient.NewUserAsync(factory);
        var b = await AuthTestClient.NewUserAsync(factory);
        var pa = await CreateProjectAsync(a, "same");
        var pb = await CreateProjectAsync(b, "same");

        Assert.Equal(HttpStatusCode.Accepted,
            (await a.PostAsync($"/api/projects/{pa.Id}/repositories", new { repoUrl = "https://github.com/o/r" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await b.PostAsync($"/api/projects/{pb.Id}/repositories", new { repoUrl = "https://github.com/o/r" })).StatusCode);
    }
}
