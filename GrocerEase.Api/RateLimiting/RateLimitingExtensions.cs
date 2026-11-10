using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace GrocerEase.Api.RateLimiting;

public static class RateLimitingExtensions
{
    /// <summary>The policy names, used by <c>[EnableRateLimiting]</c> on the login, register and refresh actions.</summary>
    public const string LoginPolicy = "login";

    public const string RegisterPolicy = "register";

    public const string RefreshPolicy = "refresh";

    public const string HealthPolicy = "health";

    /// <summary>The stable <c>code</c> in the problem response, so clients can switch on it like on every other error.</summary>
    public const string TooManyRequestsCode = "RateLimit.TooManyRequests";

    // Counting in segments of a sliding window avoids the fixed window's weakness: a burst at the end of one window
    // plus a burst at the start of the next would otherwise allow twice the limit within a few seconds.
    private const int SegmentsPerWindow = 6;

    // Used only if a rejected request has no known policy, which should not happen: the longest window a setting allows.
    private const int FallbackRetryAfterSeconds = RateLimitSettings.MaxWindowSeconds;

    /// <summary>Registers the rate limit policies and their settings. Pair it with <c>app.UseRateLimiter()</c>.</summary>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddPolicySettings(services, configuration, LoginPolicy, "RateLimiting:Login");
        AddPolicySettings(services, configuration, RegisterPolicy, "RateLimiting:Register");
        AddPolicySettings(services, configuration, RefreshPolicy, "RateLimiting:Refresh");
        AddPolicySettings(services, configuration, HealthPolicy, "RateLimiting:Health");

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptionsMonitor<RateLimitSettings>>((options, settingsByPolicy) =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AddSlidingWindowPolicy(options, LoginPolicy, settingsByPolicy.Get(LoginPolicy));
            AddSlidingWindowPolicy(options, RegisterPolicy, settingsByPolicy.Get(RegisterPolicy));
            AddSlidingWindowPolicy(options, RefreshPolicy, settingsByPolicy.Get(RefreshPolicy));
            AddSlidingWindowPolicy(options, HealthPolicy, settingsByPolicy.Get(HealthPolicy));
            options.OnRejected = (context, _) => WriteRejectionAsync(context, settingsByPolicy);
        });

        return services;
    }

    // One named instance of RateLimitSettings per policy, so each policy reads its own section.
    private static void AddPolicySettings(IServiceCollection services, IConfiguration configuration, string policy, string section) =>
        services.AddOptions<RateLimitSettings>(policy)
            .Bind(configuration.GetSection(section))
            .Validate(
                s => s.PermitLimit is >= RateLimitSettings.MinPermitLimit and <= RateLimitSettings.MaxPermitLimit,
                $"{section}:PermitLimit must be between {RateLimitSettings.MinPermitLimit} and {RateLimitSettings.MaxPermitLimit}.")
            .Validate(
                s => s.WindowSeconds is >= RateLimitSettings.MinWindowSeconds and <= RateLimitSettings.MaxWindowSeconds,
                $"{section}:WindowSeconds must be between {RateLimitSettings.MinWindowSeconds} and {RateLimitSettings.MaxWindowSeconds} (one hour).")
            .ValidateOnStart();

    private static void AddSlidingWindowPolicy(RateLimiterOptions options, string policy, RateLimitSettings settings) =>
        options.AddPolicy(policy, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
            ClientPartitionKey.For(httpContext.Connection.RemoteIpAddress),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = settings.PermitLimit,
                Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                SegmentsPerWindow = SegmentsPerWindow,
                QueueLimit = 0, // refuse at once; a queued request would only hold a connection open
            }));

    // The limiter runs before MVC, so the controller's error mapping is not involved: build the same
    // application/problem+json response here, through the same service every other error uses.
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, IOptionsMonitor<RateLimitSettings> settingsByPolicy)
    {
        var http = context.HttpContext;

        // The sliding window limiter gives no retry hint (I checked: TryGetMetadata is false), so say the window length:
        // by then every counted attempt has expired. The window belongs to the policy of the endpoint that was hit.
        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
            ? delay
            : TimeSpan.FromSeconds(policy is null ? FallbackRetryAfterSeconds : settingsByPolicy.Get(policy).WindowSeconds);
        http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests.",
                Detail = "Too many attempts. Wait and try again.",
                Extensions = { ["code"] = TooManyRequestsCode },
            },
        });
    }
}
