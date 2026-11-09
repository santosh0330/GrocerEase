using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GrocerEase.Api.Health;

/// <summary>
/// Writes a health report as JSON. The endpoints are open to anyone, so only what is safe to publish goes out: the overall status,
/// and for each check its name, status and how long it took. Never the description, the exception or the check's data: those name
/// hosts, users and libraries. They reach the log instead (the health check service logs every failure with its exception).
/// </summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private sealed record Body(string Status, double TotalDurationMs, IReadOnlyList<Entry> Checks);

    private sealed record Entry(string Name, string Status, double DurationMs);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        var body = new Body(
            report.Status.ToString(),
            Milliseconds(report.TotalDuration),
            [.. report.Entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => new Entry(e.Key, e.Value.Status.ToString(), Milliseconds(e.Value.Duration)))]);

        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, body, Options, context.RequestAborted);
    }

    private static double Milliseconds(TimeSpan duration) => Math.Round(duration.TotalMilliseconds, 1);
}
