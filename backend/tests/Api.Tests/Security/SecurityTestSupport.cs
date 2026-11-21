using System.Net;
using System.Net.Http.Json;
using Api.Tests.Projects;
using Application.Projects;
using Application.Rag;
using Application.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Security;

/// <summary>Shared helpers for the security tests: factories with specific settings, and a user with a ready repository.</summary>
public static class SecurityTestSupport
{
    public static WebApplicationFactory<Program> With(this ApiFactory factory, IRagClient? rag, params (string Key, string? Value)[] settings) =>
        factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) =>
                c.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))));
            if (rag is not null) b.ConfigureTestServices(s => s.AddSingleton(rag));
        });

    public sealed record Setup(AuthTestClient Client, Guid ProjectId, Guid RepositoryId, Guid ConversationId);

    public sealed record ProblemBody(int Status, string? Title, string? Detail, string? Code);

    /// <summary>A new user with a project, a ready repository (via the recording RAG) and one conversation.</summary>
    public static async Task<Setup> NewUserWithConversationAsync(WebApplicationFactory<Program> app, RecordingRagClient rag)
    {
        var client = await AuthTestClient.NewUserAsync(app);
        var project = (await (await client.PostAsync("/api/projects", new { name = "p" })).Content.ReadFromJsonAsync<ProjectDetailDto>())!;
        var repo = (await (await client.PostAsync($"/api/projects/{project.Id}/repositories",
            new { repoUrl = "https://github.com/octo/hello" })).Content.ReadFromJsonAsync<RepositoryDto>())!;

        rag.OnGet = RecordingRagClient.Ready;
        await client.GetAsync($"/api/repositories/{repo.Id}"); // moves the stored status to ready
        rag.OnGet = null;

        var conversation = await client.PostAsync($"/api/projects/{project.Id}/conversations", new { title = "t" });
        conversation.EnsureSuccessStatusCode();
        var id = (await conversation.Content.ReadFromJsonAsync<Application.Chat.ConversationDto>())!.Id;
        return new Setup(client, project.Id, repo.Id, id);
    }

    /// <summary>Makes every request look like it comes from <paramref name="ip"/> (TestServer has no real client address).</summary>
    public static WebApplicationFactory<Program> WithRemoteIp(this ApiFactory factory, string ip, params (string Key, string? Value)[] settings) =>
        factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) =>
                c.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))));
            b.ConfigureTestServices(s => s.AddSingleton<IStartupFilter>(new RemoteIpFilter(IPAddress.Parse(ip))));
        });

    private sealed class RemoteIpFilter(IPAddress ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = ip;
                    return nextMiddleware();
                });
                next(app);
            };
    }
}
