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

    // ---------- RAG_QUERY_MODE / RAG_QUERY_RERANK ----------

    [Fact]
    public void Query_Options_Default_To_Hybrid_Without_Rerank()
    {
        var o = factory.Services.GetRequiredService<RagQueryOptions>();

        Assert.Equal(new RagQueryOptions("hybrid", false), o);
    }

    [Theory]
    [InlineData("dense", null, "dense", false)]
    [InlineData(" Hybrid ", "TRUE", "hybrid", true)]
    [InlineData("", "", "hybrid", false)]       // blank = unset = defaults
    public void Query_Options_Are_Read_From_Configuration(string mode, string? rerank, string expectedMode, bool expectedRerank)
    {
        using var app = WithRag(("RAG_QUERY_MODE", mode), ("RAG_QUERY_RERANK", rerank));

        Assert.Equal(new RagQueryOptions(expectedMode, expectedRerank), app.Services.GetRequiredService<RagQueryOptions>());
    }

    [Theory]
    [InlineData("hybird", null)]        // typo
    [InlineData("sparse", "false")]     // not a mode RAG knows
    [InlineData("hybrid", "yes")]       // not a boolean
    [InlineData("dense", "true")]       // rerank needs hybrid
    public void Bad_Query_Options_Fail_Fast_On_Startup(string mode, string? rerank)
    {
        using var app = WithRag(("RAG_QUERY_MODE", mode), ("RAG_QUERY_RERANK", rerank));

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }

    [Fact]
    public void Rerank_Alone_Uses_The_Default_Hybrid_Mode()
    {
        using var app = WithRag(("RAG_QUERY_RERANK", "true"));

        Assert.Equal(new RagQueryOptions("hybrid", true), app.Services.GetRequiredService<RagQueryOptions>());
    }
}
