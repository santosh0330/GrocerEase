using Serilog;
using Serilog.Events;

namespace GrocerEase.Api.Logging;

/// <summary>
/// One structured event per request, written when the response has been sent: what was asked (method, path, host, who, with
/// which client) and what was answered (status, content type, how long it took). One event instead of a "request starting" and a
/// "request finished" line means one row to find in Seq, with everything on it.
/// </summary>
/// <remarks>
/// <b>What is deliberately not logged:</b> request and response <i>bodies</i> (they carry passwords, access tokens and refresh
/// tokens), the <c>Authorization</c> header and cookies (they are the credentials), and the query string (a URL is where people
/// put values they should not). If a request needs more detail than this to debug, add the one property it needs: do not turn on
/// body logging.
/// </remarks>
public static class RequestLoggingExtensions
{
    public const string MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

    // A client chooses its own User-Agent, so it is cut: one request must not be able to write a megabyte into the log.
    private const int MaxUserAgentLength = 256;

    /// <summary>
    /// Writes the per-request event. Put it <b>first</b> in the pipeline: it then times everything, and it sees the final status
    /// after the exception handler, the rate limiter and authentication have all had their say.
    /// </summary>
    public static WebApplication UseRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSerilogRequestLogging(options =>
        {
            // This host's own logger, not the shared static one (see AddStructuredLogging).
            options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
            options.MessageTemplate = MessageTemplate;
            options.GetLevel = LevelFor;
            options.EnrichDiagnosticContext = Enrich;
        });

        // Inside the request log, so the count is complete by the time the request log is written.
        app.UseMiddleware<ResponseSizeMiddleware>();

        return app;
    }

    /// <summary>
    /// How loud a request is: a server error is an error, a refused request (4xx: bad input, wrong credentials, a rate limit) is a
    /// warning worth a look, and the rest is information. The health probes are the exception: a probe every few seconds would bury
    /// everything else, so a healthy probe is debug, and an unhealthy one is a warning (the check itself logs the reason).
    /// </summary>
    public static LogEventLevel LevelFor(HttpContext context, double elapsedMilliseconds, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(context);

        var status = context.Response.StatusCode;
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            return exception is not null || status >= StatusCodes.Status500InternalServerError ? LogEventLevel.Warning : LogEventLevel.Debug;
        }

        return exception is not null || status >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error
            : status >= StatusCodes.Status400BadRequest ? LogEventLevel.Warning
            : LogEventLevel.Information;
    }

    /// <summary>Adds the request and response details to the event. Runs after the response, so authentication has already happened.</summary>
    public static void Enrich(IDiagnosticContext diagnostics, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        var response = context.Response;

        diagnostics.Set("RequestHost", request.Host.Value);
        diagnostics.Set("RequestScheme", request.Scheme);
        diagnostics.Set("Protocol", request.Protocol);
        diagnostics.Set("Endpoint", context.GetEndpoint()?.DisplayName);
        diagnostics.Set("ClientIp", context.Connection.RemoteIpAddress?.ToString());
        diagnostics.Set("UserAgent", Truncate(request.Headers.UserAgent.ToString(), MaxUserAgentLength));
        diagnostics.Set("RequestContentType", request.ContentType);
        diagnostics.Set("RequestContentLength", request.ContentLength);
        diagnostics.Set("ResponseContentType", response.ContentType);
        diagnostics.Set("ResponseContentLength", response.ContentLength);
        diagnostics.Set("ResponseBytes", context.Items.TryGetValue(ResponseSizeMiddleware.ItemKey, out var bytes) ? bytes : null);


        // Only the id of a signed-in user, never the token that proved it. Anonymous requests have none.
        var userId = context.User.FindFirst("sub")?.Value;
        if (userId is not null)
        {
            diagnostics.Set("UserId", userId);
        }
    }

    private static string? Truncate(string value, int maxLength) =>
        value.Length == 0 ? null : value.Length <= maxLength ? value : value[..maxLength];
}
