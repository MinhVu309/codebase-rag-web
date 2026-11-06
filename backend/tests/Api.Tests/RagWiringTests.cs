using Application.Rag;
using Infrastructure.Rag;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

[Collection(ApiCollection.Name)]
public class RagWiringTests(ApiFactory factory)
{
    private WebApplicationFactory<Program> WithRag(params (string Key, string? Value)[] settings) =>
        factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))));

    [Fact]
    public void Fake_Mode_Resolves_The_Fake_Client()
    {
        using var scope = factory.Services.CreateScope();

        Assert.IsType<FakeRagClient>(scope.ServiceProvider.GetRequiredService<IRagClient>());
    }

    [Fact]
    public void Http_Mode_Resolves_The_Http_Client()
    {
        using var app = WithRag(("RAG_MODE", "http"), ("RAG_BASE_URL", "http://rag.test:4010"), ("RAG_API_KEY", "k"));
        using var scope = app.Services.CreateScope();

        Assert.IsType<HttpRagClient>(scope.ServiceProvider.GetRequiredService<IRagClient>());
    }

    [Theory]
    [InlineData("http://rag.test:4010", "")]   // missing API key
    [InlineData("", "key")]                    // missing base URL
    [InlineData("not-a-url", "key")]           // malformed base URL
    public void Http_Mode_Fails_Fast_On_Bad_Configuration(string baseUrl, string apiKey)
    {
        using var app = WithRag(("RAG_MODE", "http"), ("RAG_BASE_URL", baseUrl), ("RAG_API_KEY", apiKey));

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }
}
