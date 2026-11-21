using Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Api.Tests.Security.SecurityTestSupport;

namespace Api.Tests.Security;

[Collection(ApiCollection.Name)]
public class SecurityOptionsTests(ApiFactory factory)
{
    private static SecurityOptions Bind(params (string Key, string? Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build();
        var o = new SecurityOptions();
        SecurityOptions.Bind(o, config);
        return o;
    }

    [Fact]
    public void Defaults_Are_The_Documented_Ones()
    {
        var o = Bind();

        Assert.Equal(10, o.AuthPerMinute);
        Assert.Equal(10, o.AskPerMinute);
        Assert.Equal(5, o.ImportsPerWindow);
        Assert.Equal(10, o.ImportWindowMinutes);
        Assert.Equal(1, o.MaxConcurrentImports);
        Assert.Equal(64 * 1024, o.MaxRequestBodyBytes);
        Assert.Empty(o.CorsOrigins);
        Assert.Empty(o.TrustedProxies);
        Assert.Empty(o.Errors);
    }

    [Fact]
    public void Values_Are_Read_From_The_Environment_Names_In_The_README()
    {
        var o = Bind(("RATE_LIMIT_AUTH_PER_MINUTE", "3"), ("RATE_LIMIT_ASK_PER_MINUTE", "4"), ("RATE_LIMIT_IMPORT_PER_WINDOW", "2"),
            ("RATE_LIMIT_IMPORT_WINDOW_MINUTES", "30"), ("MAX_CONCURRENT_IMPORTS_PER_USER", "2"), ("MAX_REQUEST_BODY_BYTES", "1024"),
            ("CORS_ORIGINS", "http://localhost:5173,https://app.example.test"), ("TRUSTED_PROXIES", "10.0.0.0/8, 172.18.0.2"));

        Assert.Equal([3, 4, 2, 30, 2, 1024], [o.AuthPerMinute, o.AskPerMinute, o.ImportsPerWindow, o.ImportWindowMinutes, o.MaxConcurrentImports, o.MaxRequestBodyBytes]);
        Assert.Equal(["http://localhost:5173", "https://app.example.test"], o.CorsOrigins);
        Assert.Equal(2, o.TrustedProxies.Count);
        Assert.Empty(o.Errors);
    }

    [Theory]
    [InlineData("RATE_LIMIT_AUTH_PER_MINUTE", "ten")]
    [InlineData("RATE_LIMIT_ASK_PER_MINUTE", "-1")]
    [InlineData("RATE_LIMIT_IMPORT_PER_WINDOW", "1.5")]
    [InlineData("MAX_CONCURRENT_IMPORTS_PER_USER", "-3")]
    [InlineData("MAX_REQUEST_BODY_BYTES", "64KB")]
    [InlineData("TRUSTED_PROXIES", "not-an-ip")]
    [InlineData("RATE_LIMIT_IMPORT_WINDOW_MINUTES", "0")]
    public void A_Bad_Value_Is_An_Error(string key, string value)
    {
        var o = Bind((key, value));

        Assert.NotEmpty(o.Errors);
    }

    [Theory]
    [InlineData("RATE_LIMIT_AUTH_PER_MINUTE", "ten")]
    [InlineData("MAX_REQUEST_BODY_BYTES", "-5")]
    [InlineData("TRUSTED_PROXIES", "999.1.1.1")]
    public void A_Bad_Value_Stops_The_API_At_Startup(string key, string value)
    {
        using var app = factory.With(null, (key, value));

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }

    [Fact]
    public void Limits_Can_Be_Switched_Off_With_Zero_And_The_API_Still_Starts()
    {
        using var app = factory.With(null, ("RATE_LIMIT_AUTH_PER_MINUTE", "0"), ("RATE_LIMIT_ASK_PER_MINUTE", "0"),
            ("RATE_LIMIT_IMPORT_PER_WINDOW", "0"), ("MAX_CONCURRENT_IMPORTS_PER_USER", "0"), ("MAX_REQUEST_BODY_BYTES", "0"));

        Assert.NotNull(app.CreateClient());
        Assert.Equal(0, app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.AuthPerMinute);
    }
}
