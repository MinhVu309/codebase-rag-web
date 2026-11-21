using System.Net;
using System.Net.Http.Json;
using System.Text;
using Api.Tests.Projects;
using Application.Rag;
using static Api.Tests.Security.SecurityTestSupport;

namespace Api.Tests.Security;

/// <summary>
/// Runs the whole user journey, including failures, with recognisable values and then checks that none of
/// them reached any log line (the test host logs at Trace level, so framework logs are included).
/// </summary>
[Collection(ApiCollection.Name)]
public class LogHygieneTests(ApiFactory factory)
{
    [Fact]
    public async Task No_Question_Title_Path_File_Content_Token_Password_Or_Email_Reaches_The_Logs()
    {
        var tag = Guid.NewGuid().ToString("N")[..12];
        var email = $"leak.{tag}@example.test";
        var password = $"Pw-{tag}-Aa1!";
        var wrongPassword = $"Wrong-{tag}-Aa1!";
        var projectName = $"project-{tag}";
        var title = $"title-{tag}";
        var question = $"question-{tag}";
        var failingQuestion = $"failing-question-{tag}";
        var path = $"src/path-{tag}.cs";
        var fileContent = $"file-content-{tag}";
        var snippet = $"snippet-{tag}";
        var badTitle = $"bad-title-{tag}\nsecond line";
        var malformed = $"{{\"question\":\"malformed-{tag}";

        var rag = new RecordingRagClient
        {
            OnQuery = _ => new RagAnswer($"answer-{tag} [1]", [new RagCitation(1, path, null, null, 1, 2, snippet, 0.5)], null, 5),
            OnGetFile = c => new RagFile(c.Path, c.Start, c.End, fileContent, "csharp"),
        };
        using var app = factory.With(rag);

        var client = new AuthTestClient(app);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/auth/register", new { email, password })).StatusCode);
        var secrets = client.Cookies.Values.ToList(); // access + refresh token values
        Assert.Equal(2, secrets.Count);

        Assert.Equal(HttpStatusCode.Unauthorized, (await new AuthTestClient(app).PostAsync("/api/auth/login", new { email, password = wrongPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await new AuthTestClient(app).PostAsync("/api/auth/register", new { email, password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/refresh")).StatusCode);
        secrets.AddRange(client.Cookies.Values); // the rotated tokens too

        var project = (await (await client.PostAsync("/api/projects", new { name = projectName })).Content.ReadFromJsonAsync<Application.Projects.ProjectDetailDto>())!;
        var repo = (await (await client.PostAsync($"/api/projects/{project.Id}/repositories", new { repoUrl = "https://github.com/octo/hello" })).Content.ReadFromJsonAsync<Application.Repositories.RepositoryDto>())!;
        rag.OnGet = RecordingRagClient.Ready;
        await client.GetAsync($"/api/repositories/{repo.Id}");

        var conversation = (await (await client.PostAsync($"/api/projects/{project.Id}/conversations", new { title })).Content.ReadFromJsonAsync<Application.Chat.ConversationDto>())!;
        var messages = $"/api/conversations/{conversation.Id}/messages";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync(messages, new { question })).StatusCode);

        // failure paths
        rag.QueryError = RecordingRagClient.Error(RagFailureKind.LlmError, "LLM_ERROR");
        Assert.Equal(HttpStatusCode.BadGateway, (await client.PostAsync(messages, new { question = failingQuestion })).StatusCode);
        rag.QueryError = RecordingRagClient.Error(RagFailureKind.Timeout, "TIMEOUT");
        Assert.Equal(HttpStatusCode.GatewayTimeout, (await client.PostAsync(messages, new { question = failingQuestion })).StatusCode);
        rag.QueryError = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsync($"/api/conversations/{conversation.Id}", new { title = badTitle })).StatusCode);
        var bad = new HttpRequestMessage(HttpMethod.Post, messages) { Content = new StringContent(malformed, Encoding.UTF8, "application/json") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(bad)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/repositories/{repo.Id}/files?path={Uri.EscapeDataString(path)}&start=1&end=2")).StatusCode);
        rag.FileError = RecordingRagClient.Error(RagFailureKind.NotFound, RagErrorCodes.FileNotFound);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/repositories/{repo.Id}/files?path={Uri.EscapeDataString(path)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout")).StatusCode);

        var logs = factory.Logs.Entries;
        Assert.Contains(logs, l => l.Contains("Answered conversation")); // the capture works and the journey was logged
        var sensitive = new Dictionary<string, string>
        {
            ["email"] = email, ["email local part"] = $"leak.{tag}", ["password"] = password, ["wrong password"] = wrongPassword,
            ["project name"] = projectName, ["title"] = title, ["question"] = question, ["failing question"] = failingQuestion,
            ["file path"] = $"path-{tag}", ["file content"] = fileContent, ["citation snippet"] = snippet, ["answer"] = $"answer-{tag}",
            ["rejected title"] = $"bad-title-{tag}", ["malformed body"] = $"malformed-{tag}",
        };
        for (var i = 0; i < secrets.Count; i++) sensitive[$"token/cookie #{i}"] = secrets[i];

        foreach (var (what, value) in sensitive)
            Assert.False(logs.Any(l => l.Contains(value, StringComparison.Ordinal)), $"{what} was written to a log line");
    }
}
