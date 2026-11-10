using Microsoft.AspNetCore.Http.Features;

namespace GrocerEase.Api.Logging;

/// <summary>
/// Counts the bytes the server writes as the response body, so the request log can say how much was sent. A
/// <c>Content-Length</c> header is not enough: JSON answers are streamed, and have none. It only counts; it never reads or keeps
/// the bytes, so nothing from the body can end up in a log.
/// </summary>
public sealed class ResponseSizeMiddleware(RequestDelegate next)
{
    /// <summary>Where the count is left for whoever writes the request log: <c>HttpContext.Items[Key]</c>, a <c>long</c>.</summary>
    public const string ItemKey = "GrocerEase.ResponseBytes";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var original = context.Features.GetRequiredFeature<IHttpResponseBodyFeature>();
        var counting = new CountingStream(original.Stream);
        var feature = new StreamResponseBodyFeature(counting, original);
        context.Features.Set<IHttpResponseBodyFeature>(feature);
        try
        {
            await next(context);
            await feature.CompleteAsync(); // writes out what is still buffered, so the count is final
        }
        finally
        {
            context.Items[ItemKey] = counting.BytesWritten;
            context.Features.Set(original);
        }
    }

    /// <summary>A write-only stream that passes everything to <paramref name="inner"/> and adds up how many bytes went through.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            BytesWritten += buffer.Length;
        }
    }
}
