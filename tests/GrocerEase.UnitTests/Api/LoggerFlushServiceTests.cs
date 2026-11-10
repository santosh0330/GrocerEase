using GrocerEase.Api.Logging;
using Microsoft.Extensions.Logging;

namespace GrocerEase.UnitTests.Api;

public class LoggerFlushServiceTests
{
    private sealed class DisposableFactory : ILoggerFactory
    {
        public int Disposed { get; private set; }

        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

        public ILogger CreateLogger(string categoryName) => throw new NotSupportedException();

        public void Dispose() => Disposed++;
    }

    [Fact]
    public async Task Stopping_DisposesTheLoggerFactory_WhichPostsWhatTheSinksStillHold()
    {
        var factory = new DisposableFactory();

        await new LoggerFlushService(factory).StopAsync(CancellationToken.None);

        Assert.Equal(1, factory.Disposed);
    }

    [Fact]
    public void Disposing_AlsoFlushes_ForAHostThatIsOnlyDisposed()
    {
        var factory = new DisposableFactory();

        new LoggerFlushService(factory).Dispose();

        Assert.Equal(1, factory.Disposed);
    }

    [Fact]
    public async Task Starting_DoesNothing()
    {
        var factory = new DisposableFactory();

        await new LoggerFlushService(factory).StartAsync(CancellationToken.None);

        Assert.Equal(0, factory.Disposed);
    }
}
