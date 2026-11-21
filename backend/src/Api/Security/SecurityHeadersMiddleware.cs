namespace Api.Security;

/// <summary>
/// Basic response headers for an API that only returns JSON. HSTS is not set here: TLS ends at the reverse
/// proxy, which is where Strict-Transport-Security belongs. Headers a handler already set are left alone.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            // Nothing this API returns should ever be rendered or framed; also covers browsers that ignore X-Frame-Options.
            headers.TryAdd("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
            // Responses are per user (and the auth ones set cookies): never let a shared cache keep them.
            headers.TryAdd("Cache-Control", "no-store");
            return Task.CompletedTask;
        }, context);
        return next(context);
    }
}
