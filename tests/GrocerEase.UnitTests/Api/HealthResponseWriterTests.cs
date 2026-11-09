using System.Text.Json;
using GrocerEase.Api.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GrocerEase.UnitTests.Api;

public class HealthResponseWriterTests
{
    private static async Task<(JsonElement Body, string Text, string? ContentType)> WriteAsync(HealthReport report)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponseWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var document = JsonDocument.Parse(text);
        return (document.RootElement.Clone(), text, context.Response.ContentType);
    }

    private static HealthReport Report(params (string Name, HealthStatus Status, string? Description, Exception? Exception)[] entries) =>
        new(
            entries.ToDictionary(
                e => e.Name,
                e => new HealthReportEntry(
                    e.Status,
                    e.Description,
                    TimeSpan.FromMilliseconds(12.34),
                    e.Exception,
                    new Dictionary<string, object>(StringComparer.Ordinal) { ["connectionString"] = "Host=secret-host;Password=hunter2" }),
                StringComparer.Ordinal),
            TimeSpan.FromMilliseconds(45.67));

    [Fact]
    public async Task TheBody_HasTheStatus_TheTotalDuration_AndOneEntryPerCheck()
    {
        var (body, _, contentType) = await WriteAsync(Report(("database", HealthStatus.Healthy, null, null)));

        Assert.StartsWith("application/json", contentType, StringComparison.Ordinal);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal(45.7, body.GetProperty("totalDurationMs").GetDouble());
        var check = Assert.Single(body.GetProperty("checks").EnumerateArray());
        Assert.Equal(("database", "Healthy", 12.3), (check.GetProperty("name").GetString(), check.GetProperty("status").GetString(), check.GetProperty("durationMs").GetDouble()));
    }

    [Fact]
    public async Task AnUnhealthyReport_SaysUnhealthy()
    {
        var (body, _, _) = await WriteAsync(Report(("database", HealthStatus.Unhealthy, "The database check failed.", new InvalidOperationException("boom"))));

        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", body.GetProperty("checks")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task NothingInternalIsWritten_NotTheDescription_TheException_OrTheCheckData()
    {
        var failure = new InvalidOperationException("password authentication failed for user grocerease_app at 10.0.0.5");

        var (_, text, _) = await WriteAsync(Report(("database", HealthStatus.Unhealthy, "The database check failed.", failure)));

        foreach (var secret in new[] { "password authentication", "grocerease_app", "10.0.0.5", "secret-host", "hunter2", "connectionString", "The database check failed", "InvalidOperationException", "\"description\":", "\"exception\":", "\"data\":" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ACheckWithNoEntries_WritesAnEmptyList()
    {
        var (body, _, _) = await WriteAsync(Report());

        Assert.Equal(JsonValueKind.Array, body.GetProperty("checks").ValueKind);
        Assert.Equal(0, body.GetProperty("checks").GetArrayLength());
    }

    [Fact]
    public async Task OnlyTheDocumentedFieldsAppear()
    {
        var (body, _, _) = await WriteAsync(Report(("database", HealthStatus.Healthy, null, null)));

        Assert.Equal(["checks", "status", "totalDurationMs"], body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        Assert.Equal(["durationMs", "name", "status"], body.GetProperty("checks")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task RejectsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => HealthResponseWriter.WriteAsync(null!, Report()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => HealthResponseWriter.WriteAsync(new DefaultHttpContext(), null!));
    }
}
