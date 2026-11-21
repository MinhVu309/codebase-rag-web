using Microsoft.Extensions.Options;

namespace Api.Security;

/// <summary>
/// Caps the request body (MAX_REQUEST_BODY_BYTES, default 64 KB; every body this API takes is a small JSON object).
/// A declared Content-Length over the cap is refused before anything is read; a chunked body is cut off while
/// being read. Both surface as 413 REQUEST_TOO_LARGE through <see cref="Api.Middleware.ApiExceptionHandler"/>.
/// </summary>
public class RequestSizeLimitMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
{
    public Task InvokeAsync(HttpContext context)
    {
        var max = options.Value.MaxRequestBodyBytes;
        if (max <= 0) return next(context);

        if (context.Request.ContentLength is { } length)
        {
            if (length > max) throw TooLarge();
        }
        else
        {
            context.Request.Body = new LimitedReadStream(context.Request.Body, max); // chunked: no length to check up front
        }
        return next(context);
    }

    public static BadHttpRequestException TooLarge() =>
        new("Request body too large.", StatusCodes.Status413PayloadTooLarge);

    private sealed class LimitedReadStream(Stream inner, long max) : Stream
    {
        private long _read;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await inner.ReadAsync(buffer, ct);
            Count(n);
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            Count(n);
            return n;
        }

        private void Count(int n)
        {
            _read += n;
            if (_read > max) throw TooLarge();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
