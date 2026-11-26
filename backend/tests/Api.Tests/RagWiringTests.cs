using Application.Rag;
using Infrastructure.Options;
using Infrastructure.Rag;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    // ---------- RAG_QUERY_API / CONVERSATION_SCOPE_MAX (D40; read, validated and exposed, not used by the question flow yet) ----------

    [Fact]
    public void Query_Api_Defaults_To_Single_And_Scope_Max_To_5()
    {
        Assert.Equal(RagQueryApi.Single, factory.Services.GetRequiredService<IOptions<RagOptions>>().Value.QueryApi);
        Assert.Equal(5, factory.Services.GetRequiredService<IOptions<ConversationScopeOptions>>().Value.Max);
    }

    [Theory]
    [InlineData("single", RagQueryApi.Single)]
    [InlineData("multi", RagQueryApi.Multi)]
    [InlineData(" Multi ", RagQueryApi.Multi)]
    [InlineData("SINGLE", RagQueryApi.Single)]
    [InlineData("", RagQueryApi.Single)]   // blank = unset = default
    public void Query_Api_Is_Read_From_Configuration(string value, RagQueryApi expected)
    {
        using var app = WithRag(("RAG_QUERY_API", value));

        Assert.Equal(expected, app.Services.GetRequiredService<IOptions<RagOptions>>().Value.QueryApi);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("multi-index")]
    [InlineData("1")]        // an enum number must not sneak through
    [InlineData("true")]
    public void A_Bad_Query_Api_Fails_Fast_On_Startup(string value)
    {
        using var app = WithRag(("RAG_QUERY_API", value));

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("RAG_QUERY_API", ex.ToString());
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    [InlineData(" 5 ", 5)]
    [InlineData("", 5)]      // blank = unset = default
    public void Conversation_Scope_Max_Is_Read_From_Configuration(string value, int expected)
    {
        using var app = WithRag(("CONVERSATION_SCOPE_MAX", value));

        Assert.Equal(expected, app.Services.GetRequiredService<IOptions<ConversationScopeOptions>>().Value.Max);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("6")]        // the pinned contract allows at most 5 scope items per request
    [InlineData("many")]
    [InlineData("2.5")]
    public void A_Bad_Conversation_Scope_Max_Fails_Fast_On_Startup(string value)
    {
        using var app = WithRag(("CONVERSATION_SCOPE_MAX", value));

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("CONVERSATION_SCOPE_MAX", ex.ToString());
    }

    [Fact]
    public void Query_Api_Multi_Does_Not_Change_Which_Client_Is_Resolved()
    {
        using var app = WithRag(("RAG_QUERY_API", "multi"));
        using var scope = app.Services.CreateScope();

        Assert.IsType<FakeRagClient>(scope.ServiceProvider.GetRequiredService<IRagClient>());
    }
}
