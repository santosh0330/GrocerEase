using System.Net.Http.Json;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace GrocerEase.IntegrationTests;

/// <summary>The app logs through Serilog: every event keeps its values as named properties, not only as text.</summary>
public class StructuredLoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class ThrowingHandler : ICommandHandler<LoginCommand, Result<AuthenticationResult>>
    {
        public Task<Result<AuthenticationResult>> Handle(LoginCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the handler broke");
    }

    private static async Task<LogEvent> CrashAsync(ApiFactory factory, CapturingLogSink logs)
    {
        using var app = factory.WithCapturedLogs(logs).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICommandHandler<LoginCommand, Result<AuthenticationResult>>, ThrowingHandler>()));
        using var client = app.CreateClient();

        await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest("ada@example.com", "s3cret!"));

        return Assert.Single(logs.Events, e => e.Exception is InvalidOperationException);
    }

    [Fact]
    public void TheHostLogsThroughSerilog()
    {
        using var client = factory.CreateClient();

        Assert.NotNull(factory.Services.GetService<Serilog.ILogger>());
        Assert.Contains("Serilog", factory.Services.GetRequiredService<ILoggerFactory>().GetType().FullName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEvent_KeepsItsValuesAsNamedProperties_AndTheMessageAsATemplate()
    {
        var crash = await CrashAsync(factory, new CapturingLogSink());

        // The template keeps the holes ({Method}, {Path}); the values are separate properties a file or Seq can filter on.
        Assert.Equal("Unhandled exception while handling {Method} {Path}", crash.MessageTemplate.Text);
        Assert.Equal("POST", CapturingLogSink.Text(crash, "Method"));
        Assert.Equal("/auth/login", CapturingLogSink.Text(crash, "Path"));
        Assert.Equal(LogEventLevel.Error, crash.Level);
    }

    [Fact]
    public async Task EveryEvent_CarriesTheApplicationTheEnvironmentAndTheMachine()
    {
        var crash = await CrashAsync(factory, new CapturingLogSink());

        Assert.Equal("GrocerEase.Api", CapturingLogSink.Text(crash, "Application"));
        Assert.False(string.IsNullOrEmpty(CapturingLogSink.Text(crash, "Environment")));
        Assert.Equal(Environment.MachineName, CapturingLogSink.Text(crash, "MachineName"));
    }

    [Fact]
    public async Task AnEvent_KnowsWhichClassWroteIt_AndWhenItHappened()
    {
        var before = DateTimeOffset.UtcNow;

        var crash = await CrashAsync(factory, new CapturingLogSink());

        Assert.Equal("GrocerEase.Api.ExceptionHandling.UnhandledExceptionHandler", CapturingLogSink.Text(crash, "SourceContext"));
        Assert.InRange(crash.Timestamp, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task AnException_IsKeptAsAnExceptionOnTheEvent_NotFlattenedIntoTheMessage()
    {
        var crash = await CrashAsync(factory, new CapturingLogSink());

        Assert.IsType<InvalidOperationException>(crash.Exception);
        Assert.DoesNotContain("the handler broke", crash.RenderMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLevelsComeFromConfiguration_SoTheFrameworksOwnChatterIsLeftOut()
    {
        var logs = new CapturingLogSink();
        using var app = factory.WithCapturedLogs(logs);
        using var client = app.CreateClient();

        await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        // "Microsoft" is overridden to Warning in appsettings.json: no "Request starting" / "Executing endpoint" lines.
        Assert.DoesNotContain(logs.Events, e => (CapturingLogSink.Text(e, "SourceContext") ?? "").StartsWith("Microsoft.AspNetCore.Hosting", StringComparison.Ordinal)
            && e.Level < LogEventLevel.Warning);
        Assert.DoesNotContain(logs.Events, e => (CapturingLogSink.Text(e, "SourceContext") ?? "").StartsWith("Microsoft.AspNetCore.Routing", StringComparison.Ordinal)
            && e.Level < LogEventLevel.Warning);
    }

    [Fact]
    public void AddStructuredLogging_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => GrocerEase.Api.Logging.LoggingExtensions.AddStructuredLogging(null!));
}
