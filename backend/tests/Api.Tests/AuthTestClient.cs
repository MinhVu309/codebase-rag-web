using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace Api.Tests;

/// <summary>HttpClient with a hand-rolled cookie jar so tests can see and control every Set-Cookie attribute.</summary>
public class AuthTestClient
{
    private readonly HttpClient _http;
    public Dictionary<string, string> Cookies { get; } = new();
    public IList<SetCookieHeaderValue> LastSetCookies { get; private set; } = [];

    public AuthTestClient(WebApplicationFactory<Program> factory)
    {
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
    }

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public Task<HttpResponseMessage> PostAsync(string url, object? body = null) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        });

    public Task<HttpResponseMessage> PutAsync(string url, object body) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) });

    public Task<HttpResponseMessage> DeleteAsync(string url) => SendAsync(new HttpRequestMessage(HttpMethod.Delete, url));

    /// <summary>Registers a brand-new user on <paramref name="factory"/> and returns a client logged in as them.</summary>
    public static async Task<AuthTestClient> NewUserAsync(WebApplicationFactory<Program> factory)
    {
        var client = new AuthTestClient(factory);
        var response = await client.PostAsync("/api/auth/register", new { email = NewEmail(), password = "S3cret-Passw0rd!" });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(new HttpRequestMessage(HttpMethod.Get, url));

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        if (Cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", Cookies.Select(c => $"{c.Key}={c.Value}")));

        var response = await _http.SendAsync(request);

        LastSetCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? SetCookieHeaderValue.ParseList(values.ToList())
            : [];
        foreach (var cookie in LastSetCookies)
        {
            var name = cookie.Name.Value!;
            var expired = cookie.Expires is { } e && e <= DateTimeOffset.UtcNow;
            if (expired || string.IsNullOrEmpty(cookie.Value.Value)) Cookies.Remove(name);
            else Cookies[name] = cookie.Value.Value!;
        }

        return response;
    }
}
