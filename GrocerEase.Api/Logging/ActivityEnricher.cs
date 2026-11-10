using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace GrocerEase.Api.Logging;

/// <summary>
/// Puts the id of the current trace on every event written while a request is being handled. All the events of one request then
/// share a <c>TraceId</c>: the one for the request itself, an exception, and anything the application logged in between. It is also
/// the id inside the <c>traceId</c> of an error response, so a client's report leads straight to the log lines.
/// </summary>
public sealed class ActivityEnricher : ILogEventEnricher
{
    public const string TraceIdProperty = "TraceId";
    public const string SpanIdProperty = "SpanId";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        // Outside a request there is no activity. A hierarchical (pre-W3C) id has no trace id to report.
        if (Activity.Current is not { IdFormat: ActivityIdFormat.W3C } activity)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(TraceIdProperty, activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(SpanIdProperty, activity.SpanId.ToString()));
    }
}
