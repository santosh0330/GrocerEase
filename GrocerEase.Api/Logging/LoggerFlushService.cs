using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrocerEase.Api.Logging;

/// <summary>
/// Flushes the logger when the host stops. A batching sink (Seq) holds events in memory for up to a couple of seconds before it posts
/// them, so without this the last events before a shutdown (a deployment, a crash loop, a <c>docker stop</c>) would simply be lost,
/// and they are the ones most worth having.
/// </summary>
/// <remarks>
/// <para>
/// Disposing the logger factory disposes the root Serilog logger: it posts what its sinks still hold and closes the files. It uses the <b>synchronous</b> <c>Dispose</c>
/// on purpose: the asynchronous one returns before the last batch has been posted (events arrived seconds after the host was gone),
/// the synchronous one waits for it. Blocking for the length of one HTTP post, once, at shutdown, is the right trade.
/// </para>
/// <para>
/// Registered before every other hosted service: hosted services stop in the reverse order, so this one stops last, after the
/// others have written whatever they log while stopping. It also flushes when it is disposed, for a container that is torn down
/// without the host being stopped first (as a test host is). Disposing the factory twice is safe.
/// </para>
/// </remarks>
public sealed class LoggerFlushService(ILoggerFactory loggerFactory) : IHostedService, IDisposable
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Flush();
        return Task.CompletedTask;
    }

    public void Dispose() => Flush();

    // The factory owns the root logger and its sinks. The Serilog.ILogger that can be injected is only a child of it, and disposing a
    // child does nothing: that is why the factory is disposed, not the logger.
    private void Flush() => (loggerFactory as IDisposable)?.Dispose();
}
