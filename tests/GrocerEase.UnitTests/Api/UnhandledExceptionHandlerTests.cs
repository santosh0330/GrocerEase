using GrocerEase.Api.ExceptionHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GrocerEase.UnitTests.Api;

public class UnhandledExceptionHandlerTests
{
    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class RecordingLogger : ILogger<UnhandledExceptionHandler>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }

    private readonly RecordingProblemDetailsService _service = new();
    private readonly RecordingLogger _logger = new();

    private UnhandledExceptionHandler CreateHandler() => new(_service, _logger);

    [Fact]
    public async Task AnyException_BecomesAGeneric500ThatDoesNotLeakTheMessage()
    {
        var context = new DefaultHttpContext();

        var handled = await CreateHandler().TryHandleAsync(
            context, new InvalidOperationException("connection string is Password=hunter2"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(_service.Written?.ProblemDetails);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("An unexpected error occurred.", problem.Title);
        Assert.Null(problem.Detail);
        Assert.DoesNotContain("hunter2", problem.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnyException_IsLoggedAsAnErrorWithTheException()
    {
        var exception = new InvalidOperationException("boom");

        await CreateHandler().TryHandleAsync(new DefaultHttpContext(), exception, CancellationToken.None);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(exception, entry.Exception);
    }
}
