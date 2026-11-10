using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Keeps every log event in memory, so a test can check what was logged: its level, its message, and the <b>properties</b> that a
/// file or Seq would receive as JSON. A test hands it to the app with <see cref="LogCapture.WithCapturedLogs"/>.
/// </summary>
internal sealed class CapturingLogSink : ILogEventSink
{
    public ConcurrentQueue<LogEvent> Events { get; } = new();

    /// <summary>The events as (category, level, rendered message, exception), the shape the log tests read.</summary>
    public IEnumerable<(string Category, LogLevel Level, string Message, Exception? Exception)> Entries =>
        Events.Select(e => (Category(e), ToLogLevel(e.Level), e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture), e.Exception));

    /// <summary>Everything one event says, as one text: category, level, message, exception, and every property with its value.</summary>
    public IEnumerable<string> Texts() =>
        Events.Select(e => $"{Category(e)} {e.Level} {e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)} {e.Exception} "
            + string.Join(' ', e.Properties.Select(p => $"{p.Key}={p.Value}")));

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        Events.Enqueue(logEvent);
    }

    /// <summary>The value of a scalar property, without the quotes Serilog puts around a string, or <c>null</c> if there is none.</summary>
    public static string? Text(LogEvent logEvent, string property)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        return logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue { Value: { } scalar }
            ? Convert.ToString(scalar, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static string Category(LogEvent logEvent) => Text(logEvent, "SourceContext") ?? string.Empty;

    private static LogLevel ToLogLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        _ => LogLevel.Critical,
    };
}
