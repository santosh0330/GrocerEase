using System.Text;
using GrocerEase.Api.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The request log says how many bytes were sent. That number must equal what the client received however the body was written
/// (a string, a stream, the pipe writer, in many chunks, or not at all), and counting must never change a byte of it.
/// </summary>
public class ResponseSizeMiddlewareTests
{
    /// <summary>A tiny app: the middleware under test, then <paramref name="write"/>. Returns what the client got, and what was counted.</summary>
    private static async Task<(byte[] Received, long? Counted)> RunAsync(Func<HttpContext, Task> write)
    {
        long? counted = null;
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app =>
                {
                    // Outside the middleware, like the request log: reads the count once the middleware has finished.
                    app.Use(async (context, next) =>
                    {
                        try
                        {
                            await next(context);
                        }
                        finally
                        {
                            counted = context.Items.TryGetValue(ResponseSizeMiddleware.ItemKey, out var value) ? (long)value! : null;
                        }
                    });
                    app.UseMiddleware<ResponseSizeMiddleware>();
                    app.Run(context => write(context));
                }))
            .StartAsync();

        using var response = await host.GetTestClient().GetAsync(new Uri("/", UriKind.Relative));
        return (await response.Content.ReadAsByteArrayAsync(), counted);
    }

    [Fact]
    public async Task AStringWrittenToTheResponse_IsCountedInBytes_NotCharacters()
    {
        var (received, counted) = await RunAsync(context => context.Response.WriteAsync("héllo wörld ✓")); // multi-byte characters

        Assert.Equal(Encoding.UTF8.GetBytes("héllo wörld ✓"), received);
        Assert.Equal(received.Length, counted);
        Assert.True(received.Length > "héllo wörld ✓".Length);
    }

    [Fact]
    public async Task BytesWrittenToTheBodyStream_AreCounted()
    {
        var payload = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

        var (received, counted) = await RunAsync(context => context.Response.Body.WriteAsync(payload).AsTask());

        Assert.Equal(payload, received);
        Assert.Equal(5000, counted);
    }

    [Fact]
    public async Task BytesWrittenThroughThePipeWriter_AreCounted()
    {
        var (received, counted) = await RunAsync(async context =>
        {
            var writer = context.Response.BodyWriter;
            Encoding.ASCII.GetBytes("pipe writer text", writer.GetSpan(16));
            writer.Advance(16);
            await writer.FlushAsync();
        });

        Assert.Equal("pipe writer text", Encoding.ASCII.GetString(received));
        Assert.Equal(16, counted);
    }

    [Fact]
    public async Task ALargeResponseSentInManyChunks_IsCountedWhole_AndArrivesIntact()
    {
        var chunk = Enumerable.Range(0, 32 * 1024).Select(i => (byte)(i % 253)).ToArray();

        var (received, counted) = await RunAsync(async context =>
        {
            for (var i = 0; i < 10; i++)
            {
                await context.Response.Body.WriteAsync(chunk);
                await context.Response.Body.FlushAsync();
            }
        });

        Assert.Equal(10 * chunk.Length, received.Length);
        Assert.Equal(10L * chunk.Length, counted);
        Assert.True(received.Chunk(chunk.Length).All(part => part.SequenceEqual(chunk)));
    }

    [Fact]
    public async Task AnEmptyResponse_IsCountedAsZero_NotAsMissing()
    {
        var (received, counted) = await RunAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        Assert.Empty(received);
        Assert.Equal(0L, counted);
    }

    [Fact]
    public async Task WhenTheHandlerThrows_TheCountIsStillLeftBehind_AndTheExceptionStillPropagates()
    {
        long? counted = null;
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        try
                        {
                            await next(context);
                        }
                        catch (InvalidOperationException)
                        {
                            counted = context.Items.TryGetValue(ResponseSizeMiddleware.ItemKey, out var value) ? (long)value! : null;
                            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        }
                    });
                    app.UseMiddleware<ResponseSizeMiddleware>();
                    app.Run(_ => throw new InvalidOperationException("the handler broke"));
                }))
            .StartAsync();

        using var response = await host.GetTestClient().GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0L, counted); // nothing was written, and the count is there all the same
    }

    [Fact]
    public async Task TheMiddleware_RejectsNull() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ResponseSizeMiddleware(_ => Task.CompletedTask).InvokeAsync(null!));
}
