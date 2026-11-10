using System.Diagnostics;
using GrocerEase.Api.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace GrocerEase.UnitTests.Api;

public class ActivityEnricherTests
{
    private sealed class Factory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }

    private static LogEvent Event() =>
        new(DateTimeOffset.UtcNow, LogEventLevel.Information, null, new MessageTemplate("x", []), []);

    [Fact]
    public void WithinATrace_TheEventGetsTheTraceIdAndTheSpanId()
    {
        using var activity = new Activity("request");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        var logEvent = Event();

        new ActivityEnricher().Enrich(logEvent, new Factory());

        Assert.Equal(activity.TraceId.ToString(), ((ScalarValue)logEvent.Properties["TraceId"]).Value);
        Assert.Equal(activity.SpanId.ToString(), ((ScalarValue)logEvent.Properties["SpanId"]).Value);
        Assert.Equal(32, activity.TraceId.ToString().Length);
    }

    [Fact]
    public void OutsideATrace_TheEventGetsNeither()
    {
        Assert.Null(Activity.Current);
        var logEvent = Event();

        new ActivityEnricher().Enrich(logEvent, new Factory());

        Assert.DoesNotContain("TraceId", logEvent.Properties.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain("SpanId", logEvent.Properties.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public void ATraceIdThatIsAlreadyOnTheEvent_IsKept()
    {
        using var activity = new Activity("request");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        var logEvent = Event();
        logEvent.AddPropertyIfAbsent(new LogEventProperty("TraceId", new ScalarValue("already-there")));

        new ActivityEnricher().Enrich(logEvent, new Factory());

        Assert.Equal("already-there", ((ScalarValue)logEvent.Properties["TraceId"]).Value);
    }

    [Fact]
    public void AHierarchicalActivity_HasNoTraceIdToReport()
    {
        using var activity = new Activity("request");
        activity.SetIdFormat(ActivityIdFormat.Hierarchical);
        activity.Start();
        var logEvent = Event();

        new ActivityEnricher().Enrich(logEvent, new Factory());

        Assert.DoesNotContain("TraceId", logEvent.Properties.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public void Enrich_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ActivityEnricher().Enrich(null!, new Factory()));
        Assert.Throws<ArgumentNullException>(() => new ActivityEnricher().Enrich(Event(), null!));
    }
}
