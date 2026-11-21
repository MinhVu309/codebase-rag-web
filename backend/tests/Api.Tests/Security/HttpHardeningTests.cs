using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using static Api.Tests.Security.SecurityTestSupport;

namespace Api.Tests.Security;

/// <summary>CORS, security headers, request size, content type and cookie flags.</summary>
[Collection(ApiCollection.Name)]
public class HttpHardeningTests(ApiFactory factory)
{
    private const string DevOrigin = "http://localhost:5173";

    // ---------- security headers ----------

    [Theory]
    [InlineData("/health", HttpStatusCode.OK)]
    [InlineData("/api/auth/me", HttpStatusCode.Unauthorized)]
    [InlineData("/api/does-not-exist", HttpStatusCode.NotFound)]
    public async Task Every_Response_Carries_The_Basic_Security_Headers(string url, HttpStatusCode expected)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Auth_Responses_That_Set_Cookies_Are_Not_Cacheable_And_Error_Bodies_Keep_Their_Headers()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = AuthTestClient.NewEmail(), password = "S3cret-Passw0rd!" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());

        var bad = await client.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.True(bad.Headers.Contains("X-Content-Type-Options"));
    }

    // ---------- CORS ----------

    private static HttpRequestMessage Preflight(string origin, string method = "POST", string headers = "content-type")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", headers);
        return request;
    }

    [Fact]
    public async Task Without_CORS_ORIGINS_No_Cross_Origin_Header_Is_Ever_Sent()
    {
        var http = factory.CreateClient();

        var pre = await http.SendAsync(Preflight(DevOrigin));
        var get = new HttpRequestMessage(HttpMethod.Get, "/health");
        get.Headers.Add("Origin", DevOrigin);
        var simple = await http.SendAsync(get);

        Assert.False(pre.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(simple.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_Listed_Origin_Gets_A_Credentialed_Preflight_With_Only_The_Needed_Methods_And_Headers()
    {
        using var app = factory.With(null, ("CORS_ORIGINS", $"{DevOrigin}, https://app.example.test/"));
        var http = app.CreateClient();

        var pre = await http.SendAsync(Preflight(DevOrigin, "PATCH", "content-type,x-request-id"));

        Assert.True(pre.IsSuccessStatusCode, $"preflight was {(int)pre.StatusCode}");
        Assert.Equal(DevOrigin, pre.Headers.GetValues("Access-Control-Allow-Origin").Single()); // echoed, never "*"
        Assert.Equal("true", pre.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        var methods = pre.Headers.GetValues("Access-Control-Allow-Methods").Single();
        foreach (var m in new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }) Assert.Contains(m, methods);
        Assert.DoesNotContain("*", methods);
        var allowedHeaders = pre.Headers.GetValues("Access-Control-Allow-Headers").Single();
        Assert.Contains("Content-Type", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*", allowedHeaders);

        // the trailing slash in the setting was normalised to a real origin
        var second = await http.SendAsync(Preflight("https://app.example.test"));
        Assert.Equal("https://app.example.test", second.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Theory]
    [InlineData("http://evil.example.test")]
    [InlineData("http://localhost:5174")]          // other port = other origin
    [InlineData("https://localhost:5173")]         // other scheme = other origin
    [InlineData("null")]
    public async Task An_Origin_That_Is_Not_Listed_Gets_No_Allow_Origin_Header(string origin)
    {
        using var app = factory.With(null, ("CORS_ORIGINS", DevOrigin));

        var pre = await app.CreateClient().SendAsync(Preflight(origin));

        Assert.False(pre.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(pre.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task A_Method_Or_Header_Outside_The_Allow_List_Is_Refused_In_The_Preflight()
    {
        using var app = factory.With(null, ("CORS_ORIGINS", DevOrigin));
        var http = app.CreateClient();

        var trace = await http.SendAsync(Preflight(DevOrigin, "TRACE"));
        var custom = await http.SendAsync(Preflight(DevOrigin, "POST", "x-api-key"));

        // The server always answers with its fixed allow-lists; the browser refuses the real request because
        // TRACE and X-Api-Key are not in them.
        Assert.DoesNotContain("TRACE", trace.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.DoesNotContain("X-Api-Key", custom.Headers.GetValues("Access-Control-Allow-Headers").Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Real_Request_From_A_Listed_Origin_Gets_Allow_Origin_And_Exposes_Retry_After()
    {
        using var app = factory.With(null, ("CORS_ORIGINS", DevOrigin));
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", DevOrigin);

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(DevOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.Contains("Retry-After", response.Headers.GetValues("Access-Control-Expose-Headers").Single());
    }

    [Theory]
    [InlineData("*")]
    [InlineData("localhost:5173")]                 // no scheme
    [InlineData("http://localhost:5173/app")]      // path
    [InlineData("ftp://localhost")]
    [InlineData("http://*.example.test")]
    public void A_Bad_CORS_ORIGINS_Value_Stops_The_API_At_Startup(string value)
    {
        using var app = factory.With(null, ("CORS_ORIGINS", value));

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }

    // ---------- request size ----------

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>A body without Content-Length (chunked), like a client streaming its upload.</summary>
    private sealed class NonSeekableContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    [Fact]
    public async Task A_Body_Over_The_Limit_Is_413_Before_It_Is_Read_And_A_Body_Under_It_Is_Handled_Normally()
    {
        using var app = factory.With(null, ("MAX_REQUEST_BODY_BYTES", "300"));
        var http = app.CreateClient();
        string Body(int padding) => "{\"email\":\"" + new string('a', padding) + "@example.com\",\"password\":\"wrong-password-1\"}";

        var ok = await http.PostAsync("/api/auth/login", Json(Body(20)));
        Assert.Equal(HttpStatusCode.Unauthorized, ok.StatusCode); // read and judged: wrong credentials, not "too large"

        var big = await http.PostAsync("/api/auth/login", Json(Body(400)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        Assert.Equal("application/problem+json", big.Content.Headers.ContentType?.MediaType);
        Assert.Equal("REQUEST_TOO_LARGE", (await big.Content.ReadFromJsonAsync<ProblemBody>())!.Code);
    }

    [Fact]
    public async Task A_Chunked_Body_Without_Content_Length_Is_Cut_Off_At_The_Limit()
    {
        using var app = factory.With(null, ("MAX_REQUEST_BODY_BYTES", "300"));
        var content = new NonSeekableContent(Encoding.UTF8.GetBytes("{\"email\":\"" + new string('a', 1000) + "@example.com\",\"password\":\"x\"}"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await app.CreateClient().PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task The_Default_Limit_Is_64_KB_And_Zero_Turns_It_Off()
    {
        var padded = Json("{\"email\":\"" + new string('a', 70 * 1024) + "@example.com\",\"password\":\"x\"}");
        var defaultLimit = await factory.CreateClient().PostAsync("/api/auth/login", padded);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, defaultLimit.StatusCode);

        using var off = factory.With(null, ("MAX_REQUEST_BODY_BYTES", "0"));
        var unlimited = await off.CreateClient().PostAsync("/api/auth/login", Json("{\"email\":\"" + new string('a', 70 * 1024) + "@example.com\",\"password\":\"x\"}"));
        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, unlimited.StatusCode); // 400: email too long, judged by the validator
    }

    // ---------- content type: a cross-site HTML form cannot send JSON ----------

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task State_Changing_Endpoints_Refuse_Non_Json_Bodies(string contentType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent("{\"email\":\"a@b.co\",\"password\":\"x\"}", Encoding.UTF8, contentType),
        };

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // ---------- cookies ----------

    [Fact]
    public async Task Cookie_Secure_Flag_Is_On_Unless_AUTH_COOKIE_SECURE_Is_Exactly_false()
    {
        foreach (var (value, expectedSecure) in new (string?, bool)[] { (null, true), ("true", true), ("0", true), ("no", true), ("", true), ("false", false), ("FALSE", false) })
        {
            using var app = factory.With(null, ("AUTH_COOKIE_SECURE", value));
            var client = new AuthTestClient(app);
            (await client.PostAsync("/api/auth/register", new { email = AuthTestClient.NewEmail(), password = "S3cret-Passw0rd!" })).EnsureSuccessStatusCode();

            Assert.All(client.LastSetCookies, c =>
            {
                Assert.True(c.HttpOnly, $"HttpOnly for AUTH_COOKIE_SECURE={value ?? "(unset)"}");
                Assert.Equal(expectedSecure, c.Secure);
                Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, c.SameSite);
            });
        }
    }

    [Fact]
    public void Turning_The_Secure_Flag_Off_Is_Announced_In_The_Startup_Log()
    {
        using var off = factory.With(null, ("AUTH_COOKIE_SECURE", "false"));
        off.CreateClient();

        Assert.Contains(factory.Logs.Entries, l => l.Contains("AUTH_COOKIE_SECURE=false"));
    }

    [Fact]
    public async Task Logout_Clears_Both_Cookies_With_The_Same_Flags_And_Paths()
    {
        var client = await AuthTestClient.NewUserAsync(factory);

        (await client.PostAsync("/api/auth/logout")).EnsureSuccessStatusCode();

        var access = Assert.Single(client.LastSetCookies, c => c.Name == "access_token");
        var refresh = Assert.Single(client.LastSetCookies, c => c.Name == "refresh_token");
        Assert.All(new[] { access, refresh }, c =>
        {
            Assert.True(c.HttpOnly);
            Assert.True(c.Secure);
            Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, c.SameSite);
            Assert.True(c.Expires <= DateTimeOffset.UtcNow);
        });
        Assert.Equal("/", access.Path.Value);
        Assert.Equal("/api/auth", refresh.Path.Value);
    }
}
