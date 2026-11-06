namespace Api.Middleware;

/// <summary>Reads or creates <c>X-Request-ID</c>, echoes it on the response and adds it to the log scope.</summary>
public class RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> logger)
{
    public const string HeaderName = "X-Request-ID";
    private const int MaxLength = 100;

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaxLength || id.Any(char.IsControl))
            id = Guid.NewGuid().ToString("N");

        context.Items[HeaderName] = id;
        context.Response.Headers[HeaderName] = id;

        using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = id }))
        {
            await next(context);
        }
    }
}
