using System.Globalization;
using Serilog;
using Serilog.Formatting.Json;
using Microsoft.Extensions.Options;

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

        // Checked when the app starts, like every other setting: a bad value stops the app instead of silently losing the logs.
        builder.Services.AddOptions<LogFileSettings>()
            .Bind(builder.Configuration.GetSection(LogFileSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<SeqSettings>()
            .Bind(builder.Configuration.GetSection(SeqSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // First, so it is the last to stop: the final events are posted after everything else has finished logging.
        builder.Services.AddHostedService<LoggerFlushService>();

        builder.Host.UseSerilog(
            (context, services, logger) => logger
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext() // properties pushed by the request pipeline (TraceId, RequestId) ride along on every event
                .Enrich.With<ActivityEnricher>() // TraceId and SpanId, when there is a trace
                .Enrich.WithProperty("Application", ApplicationName)
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                .Enrich.WithProperty("MachineName", Environment.MachineName)
                .WriteTo.Console(outputTemplate: ConsoleTemplate, formatProvider: CultureInfo.InvariantCulture) // numbers and dates read the same on every machine
                .WriteToFile(services.GetRequiredService<IOptions<LogFileSettings>>().Value)
                .WriteToSeq(services.GetRequiredService<IOptions<SeqSettings>>().Value),
            // Several hosts can live in one process (every integration test starts its own). Each keeps its own logger and none
            // of them takes over the shared static Log.Logger, so one host's events can never land in another's sinks.
            preserveStaticLogger: true);

        return builder;
    }

    /// <summary>
    /// One JSON document per line, in a new file every day. A line carries the timestamp (with its offset), the level, the message
    /// template and the rendered message, every property, and the exception with its stack trace as a JSON string, so a stack trace
    /// is still one line and <c>jq</c>, <c>grep</c> and Seq read it the same way. With <c>LogFiles:Enabled</c> off nothing is added:
    /// no file and no folder is created, and the console and Seq are unaffected.
    /// </summary>
    private static LoggerConfiguration WriteToFile(this LoggerConfiguration logger, LogFileSettings settings) =>
        !settings.Enabled ? logger : logger.WriteTo.File(
            new JsonFormatter(renderMessage: true, formatProvider: CultureInfo.InvariantCulture),
            Path.Combine(settings.Directory, LogFileSettings.FileNamePrefix + LogFileSettings.FileNameSuffix),
            rollingInterval: RollingInterval.Day, // grocerease-20261110.log: the file name carries the date, and a new day starts a new file
            retainedFileCountLimit: null, // retention is by age below, not by how many files there happen to be
            retainedFileTimeLimit: TimeSpan.FromDays(settings.RetainedDays),
            fileSizeLimitBytes: settings.MaxFileSizeKilobytes * 1024L,
            rollOnFileSizeLimit: true,
            // Several writers can share one file without cutting each other's lines in half: two API instances on one volume, or
            // (as in the tests) several hosts in one process. Each event is written with one append. Without it, their lines interleave
            // and a line that is not JSON ends up in the file, which breaks every tool that reads one document per line.
            shared: true);

    /// <summary>
    /// Sends events to Seq in batches from a background task, so a slow or unreachable Seq never delays a request. If Seq cannot be
    /// reached the sink keeps trying and, past its in-memory limit, drops the oldest events: logging must never be the reason an
    /// API stops answering. The console and the files still have everything.
    /// </summary>
    private static LoggerConfiguration WriteToSeq(this LoggerConfiguration logger, SeqSettings settings) =>
        settings.IsEnabled
            ? logger.WriteTo.Seq(settings.ServerUrl, apiKey: string.IsNullOrWhiteSpace(settings.ApiKey) ? null : settings.ApiKey, formatProvider: CultureInfo.InvariantCulture)
            : logger;
}
