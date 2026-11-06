using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Application.Common;
using Application.Rag;
using Domain.Enums;
using Infrastructure.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Rag;

/// <summary>
/// Hand-written client for RAG Contract v0.2. Sends <c>X-API-Key</c> (except on /v1/health) and
/// <c>X-Request-ID</c>. Logs method, path, status, duration and request id only: never the question,
/// file content, API key or response bodies.
/// </summary>
public class HttpRagClient(
    HttpClient http,
    IOptions<RagOptions> options,
    ILogger<HttpRagClient> logger,
    IHttpContextAccessor? httpContext = null) : IRagClient
{
    private readonly RagOptions _options = options.Value;

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            await SendAsync(HttpMethod.Get, "v1/health", null, _options.RequestTimeout, sendKey: false, ct);
            return true;
        }
        catch (RagException)
        {
            return false;
        }
    }

    public async Task<RagIndexCreated> CreateIndexAsync(string repoUrl, string gitRef, string clientRef, CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Post, "v1/indexes", new CreateIndexBody(repoUrl, gitRef, clientRef),
            _options.RequestTimeout, true, ct);
        var dto = Parse<CreateIndexResponse>(body);
        if (string.IsNullOrWhiteSpace(dto.IndexId)) throw Invalid("missing index_id");
        return new RagIndexCreated(dto.IndexId, ParseStatus(dto.Status));
    }

    public async Task<RagIndexStatus> GetIndexAsync(string indexId, CancellationToken ct = default)
    {
        var path = $"v1/indexes/{Uri.EscapeDataString(indexId)}";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var body = await SendAsync(HttpMethod.Get, path, null, _options.StatusTimeout, true, ct);
                var dto = Parse<IndexStatusResponse>(body);
                return new RagIndexStatus(
                    dto.IndexId ?? indexId,
                    ParseStatus(dto.Status),
                    dto.Stage,
                    dto.Progress?.FilesDone ?? 0,
                    dto.Progress?.TotalFiles ?? 0,
                    dto.CommitSha,
                    dto.Stats?.Files,
                    dto.Stats?.Chunks,
                    dto.Error is { Code: { } code } ? new RagIndexError(code, dto.Error.Message) : null);
            }
            catch (RagException ex) when (attempt < _options.StatusRetries && IsTransient(ex))
            {
                logger.LogWarning("RAG status check failed ({Code}), retry {Attempt}/{Max}",
                    ex.Code, attempt + 1, _options.StatusRetries);
                if (_options.StatusRetryDelay > TimeSpan.Zero)
                    await Task.Delay(_options.StatusRetryDelay * (attempt + 1), ct);
            }
        }
    }

    public async Task<RagAnswer> QueryAsync(string indexId, string question, int topK, RagQueryOptions? options = null, CancellationToken ct = default)
    {
        var o = options ?? new RagQueryOptions();
        var body = await SendAsync(HttpMethod.Post, "v1/query",
            new QueryBody(indexId, question, topK, new QueryOptionsDto(o.Mode, o.Rerank), o.Debug),
            _options.QueryTimeout, true, ct);
        var dto = Parse<QueryResponse>(body);
        if (dto.Answer is null) throw Invalid("missing answer");

        var citations = (dto.Citations ?? [])
            .Select(c => new RagCitation(c.RefId, c.FilePath ?? "", c.Symbol, c.SymbolKind,
                c.StartLine, c.EndLine, c.Snippet ?? "", c.Score))
            .ToList();
        return new RagAnswer(dto.Answer, citations, dto.Usage?.Clone(), dto.LatencyMs);
    }

    public async Task<RagFile> GetFileAsync(string indexId, string path, int start, int end, CancellationToken ct = default)
    {
        var url = $"v1/indexes/{Uri.EscapeDataString(indexId)}/files" +
                  $"?path={Uri.EscapeDataString(path)}&start={start}&end={end}";
        var body = await SendAsync(HttpMethod.Get, url, null, _options.RequestTimeout, true, ct);
        var dto = Parse<FileResponse>(body);
        if (dto.Content is null) throw Invalid("missing content");
        return new RagFile(dto.Path ?? path, dto.Start, dto.End, dto.Content, dto.Language);
    }

    public async Task DeleteIndexAsync(string indexId, CancellationToken ct = default)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"v1/indexes/{Uri.EscapeDataString(indexId)}", null,
                _options.RequestTimeout, true, ct);
        }
        catch (RagException ex) when (ex.Kind == RagFailureKind.NotFound)
        {
            // DELETE is idempotent: already gone is success.
        }
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<string> SendAsync(HttpMethod method, string relativeUrl, object? payload,
        TimeSpan timeout, bool sendKey, CancellationToken ct)
    {
        var requestId = CurrentRequestId();
        var logPath = relativeUrl.Split('?')[0];
        var sw = Stopwatch.StartNew();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            using var request = new HttpRequestMessage(method, relativeUrl);
            request.Headers.Add(RequestIds.HeaderName, requestId);
            if (sendKey) request.Headers.Add("X-API-Key", _options.ApiKey);
            if (payload is not null)
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload, payload.GetType(), RagJson.Options), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
            var text = await response.Content.ReadAsStringAsync(cts.Token);

            logger.LogInformation("RAG {Method} {Path} -> {Status} in {Ms}ms [{RequestId}]",
                method.Method, logPath, (int)response.StatusCode, sw.ElapsedMilliseconds, requestId);

            if (!response.IsSuccessStatusCode) throw FromErrorResponse(response.StatusCode, text);
            return text;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("RAG {Method} {Path} timed out after {Ms}ms [{RequestId}]",
                method.Method, logPath, sw.ElapsedMilliseconds, requestId);
            throw new RagException(RagFailureKind.Timeout, RagErrorCodes.Timeout);
        }
        catch (HttpRequestException ex)
        {
            // ex.Message can contain the URL host but never headers or bodies.
            logger.LogWarning("RAG {Method} {Path} unreachable: {Reason} [{RequestId}]",
                method.Method, logPath, ex.GetType().Name, requestId);
            throw new RagException(RagFailureKind.Unavailable, RagErrorCodes.Unavailable);
        }
    }

    private string CurrentRequestId() =>
        httpContext?.HttpContext?.Items[RequestIds.HeaderName] as string ?? Guid.NewGuid().ToString("N");

    private static RagException FromErrorResponse(HttpStatusCode status, string body)
    {
        string? code = null, message = null;
        try
        {
            var envelope = JsonSerializer.Deserialize<ErrorEnvelope>(body, RagJson.Options);
            code = envelope?.Error?.Code;
            message = envelope?.Error?.Message;
        }
        catch (JsonException)
        {
            // Not the contract's error format (e.g. an HTML error page from a proxy): fall back to the status.
        }

        var kind = code switch
        {
            RagErrorCodes.Unauthorized => RagFailureKind.Unauthorized,
            RagErrorCodes.RepoNotFound or RagErrorCodes.IndexNotFound or RagErrorCodes.FileNotFound => RagFailureKind.NotFound,
            RagErrorCodes.IndexNotReady => RagFailureKind.NotReady,
            RagErrorCodes.RepoTooLarge or RagErrorCodes.InvalidRepo or RagErrorCodes.InvalidRange => RagFailureKind.Invalid,
            RagErrorCodes.LlmError => RagFailureKind.LlmError,
            RagErrorCodes.Timeout => RagFailureKind.Timeout,
            RagErrorCodes.InternalError => RagFailureKind.Internal,
            _ => KindFromStatus(status),
        };

        return new RagException(kind, code ?? DefaultCode(kind), (int)status, message);
    }

    private static RagFailureKind KindFromStatus(HttpStatusCode status) => (int)status switch
    {
        401 => RagFailureKind.Unauthorized,
        404 => RagFailureKind.NotFound,
        409 => RagFailureKind.NotReady,
        400 or 422 => RagFailureKind.Invalid,
        502 => RagFailureKind.LlmError,
        504 => RagFailureKind.Timeout,
        503 => RagFailureKind.Unavailable,
        _ => RagFailureKind.Internal,
    };

    private static string DefaultCode(RagFailureKind kind) => kind switch
    {
        RagFailureKind.Unauthorized => RagErrorCodes.Unauthorized,
        RagFailureKind.NotFound => RagErrorCodes.IndexNotFound,
        RagFailureKind.NotReady => RagErrorCodes.IndexNotReady,
        RagFailureKind.Invalid => RagErrorCodes.InvalidRepo,
        RagFailureKind.LlmError => RagErrorCodes.LlmError,
        RagFailureKind.Timeout => RagErrorCodes.Timeout,
        RagFailureKind.Unavailable => RagErrorCodes.Unavailable,
        _ => RagErrorCodes.InternalError,
    };

    /// <summary>
    /// Only failures that plausibly succeed on a second try; 401/404/409/4xx never do. Used for GET status only,
    /// where the contract defines no LLM_ERROR, so a 502 there comes from a proxy or crash and is transient too.
    /// </summary>
    private static bool IsTransient(RagException ex) =>
        ex.Kind is RagFailureKind.Timeout or RagFailureKind.Unavailable or RagFailureKind.Internal or RagFailureKind.LlmError;

    private static T Parse<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, RagJson.Options) ?? throw Invalid("empty body");
        }
        catch (JsonException)
        {
            throw Invalid("malformed JSON");
        }
    }

    private static IndexStatus ParseStatus(string? value) =>
        Enum.TryParse<IndexStatus>(value, ignoreCase: true, out var status) && Enum.IsDefined(status)
            ? status
            : throw Invalid("unknown status");

    private static RagException Invalid(string why) =>
        new(RagFailureKind.InvalidResponse, RagErrorCodes.InvalidResponse, upstreamMessage: why);
}
