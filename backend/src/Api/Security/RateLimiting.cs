using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Api.Security;

public static class RateLimitPolicies
{
    /// <summary>register, login, refresh: per client IP. Brute-force and account-creation spam target these.</summary>
    public const string Auth = "auth";
    /// <summary>Sending a question: per user. Each one costs an LLM call.</summary>
    public const string Ask = "ask";
    /// <summary>Importing a repository: per user. Each one starts a clone + embedding job in RAG.</summary>
    public const string Import = "import";
}

public static class RateLimiting
{
    public const string RateLimitedCode = "RATE_LIMITED";

    /// <summary>
    /// Registers the three named policies. Configuration is read lazily (through <see cref="SecurityOptions"/>) so
    /// that environment variables and test overrides apply. A limit of 0 turns that policy into a no-op, but the
    /// policy still exists because controllers reference it by name.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<SecurityOptions>>((limiter, security) =>
        {
            var s = security.Value;
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRejectionAsync;

            limiter.AddPolicy(RateLimitPolicies.Auth, http =>
                FixedWindow(s.AuthPerMinute, TimeSpan.FromMinutes(1), ClientIp(http)));
            limiter.AddPolicy(RateLimitPolicies.Ask, http =>
                FixedWindow(s.AskPerMinute, TimeSpan.FromMinutes(1), UserKey(http)));
            limiter.AddPolicy(RateLimitPolicies.Import, http =>
                FixedWindow(s.ImportsPerWindow, TimeSpan.FromMinutes(Math.Max(1, s.ImportWindowMinutes)), UserKey(http)));
        });
        return services;
    }

    private static RateLimitPartition<string> FixedWindow(int permits, TimeSpan window, string key) =>
        permits <= 0
            ? RateLimitPartition.GetNoLimiter(key)
            : RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,
                QueueLimit = 0, // reject at once, do not hold the request
                AutoReplenishment = true,
            });

    // Behind the reverse proxy this is the real client IP only when TRUSTED_PROXIES is set (forwarded headers middleware).
    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Runs after authentication; the route is [Authorize]d so the "sub" claim is there. IP is a safe fallback.
    private static string UserKey(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true && Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id)
            ? "user:" + id
            : "ip:" + ClientIp(http);

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken ct)
    {
        var http = context.HttpContext;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var span) ? span : TimeSpan.FromSeconds(60);
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Quá nhiều yêu cầu",
            Detail = $"Bạn gửi quá nhiều yêu cầu. Thử lại sau {seconds} giây.",
        };
        problem.Extensions["code"] = RateLimitedCode;
        await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
        });
    }
}
