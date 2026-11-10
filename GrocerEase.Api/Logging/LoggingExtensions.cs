using System.Globalization;
using Serilog;

namespace GrocerEase.Api.Logging;

/// <summary>
/// Replaces ASP.NET Core's default logging with Serilog. Application code keeps writing to <c>ILogger&lt;T&gt;</c>; only where the
/// events go, and in what shape, changes. Only Api knows Serilog: the other layers log through the standard abstraction.
/// </summary>
public static class LoggingExtensions
{
    /// <summary>The readable form for the console. Files and Seq get the same events as JSON, with every property kept.</summary>
    public const string ConsoleTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}";

    /// <summary>Name of the application, put on every event so logs from several services can share one Seq.</summary>
    public const string ApplicationName = "GrocerEase.Api";

    /// <summary>
    /// Sends every log event through Serilog. Levels (<c>Serilog:MinimumLevel</c>) come from configuration. Sinks registered in the
    /// service collection (<c>ILogEventSink</c>) are added too, which is how a test reads what the app logged.
    /// </summary>
    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Host.UseSerilog(
            (context, services, logger) => logger
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext() // properties pushed by the request pipeline (TraceId, RequestId) ride along on every event
                .Enrich.WithProperty("Application", ApplicationName)
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                .Enrich.WithProperty("MachineName", Environment.MachineName)
                .WriteTo.Console(outputTemplate: ConsoleTemplate, formatProvider: CultureInfo.InvariantCulture), // numbers and dates read the same on every machine
            // Several hosts can live in one process (every integration test starts its own). Each keeps its own logger and none
            // of them takes over the shared static Log.Logger, so one host's events can never land in another's sinks.
            preserveStaticLogger: true);

        return builder;
    }
}
