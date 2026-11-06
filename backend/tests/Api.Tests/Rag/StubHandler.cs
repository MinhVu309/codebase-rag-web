using System.Net;
using System.Text;

namespace Api.Tests.Rag;

public record CapturedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body);

/// <summary>Scripted HTTP responses for HttpRagClient tests. Each call consumes the next responder (last one repeats).</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly List<Func<CancellationToken, Task<HttpResponseMessage>>> _responders = [];
    public List<CapturedRequest> Requests { get; } = [];

    public StubHandler Respond(HttpStatusCode status, string? json = null)
    {
        _responders.Add(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json ?? "", Encoding.UTF8, "application/json"),
        }));
        return this;
    }

    public StubHandler Throw(Exception ex)
    {
        _responders.Add(_ => throw ex);
        return this;
    }

    /// <summary>Never answers; completes only when the caller's token is cancelled (simulates a hung RAG).</summary>
    public StubHandler Hang()
    {
        _responders.Add(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, headers, body));

        var index = Math.Min(Requests.Count - 1, _responders.Count - 1);
        return await _responders[index](ct);
    }
}
