using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GrocerEase.Api.Authentication;

public static class AuthenticationExtensions
{
    // Stable codes in the problem response, so a client can switch on them like on every other error. Telling "no token",
    // "bad token" and "expired token" apart is deliberate: an expired token means "sign in again", the others do not.
    public const string RequiredCode = "Authentication.Required";
    public const string InvalidTokenCode = "Authentication.InvalidToken";
    public const string TokenExpiredCode = "Authentication.TokenExpired";
    public const string ForbiddenCode = "Authorization.Forbidden";

    /// <summary>
    /// Authenticates requests that carry a bearer token. What a valid token is comes from <see cref="JwtSettings"/>, the same
    /// settings the token generator signs with, so the two cannot disagree. Pair it with <c>app.UseAuthentication()</c> and
    /// <c>app.UseAuthorization()</c>. A route is then protected with <c>[Authorize]</c>, and one that needs a permission names it:
    /// <c>[Authorize(Policy = Permissions.RolesRead)]</c>.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured here, with the settings injected, so nothing about the key is read while services are being registered.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, jwt) =>
            {
                // Keep the claim names as the generator writes them ("sub", "email"), not the long .NET claim URIs.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = JwtValidationParameters.From(jwt.Value);
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = WriteUnauthorizedAsync,
                    OnForbidden = WriteForbiddenAsync,
                };
            });

        // One policy per permission, named after it: [Authorize(Policy = Permissions.RolesRead)] asks "does the token carry the
        // permission roles.read?". The endpoint names what it needs, not which roles happen to have it, so giving the permission to
        // another role later changes no endpoint. A caller who fails a policy gets the same 403 problem as any other (OnForbidden).
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, policy => policy.RequireClaim(JwtClaims.Permission, permission));
            }
        });
        return services;
    }

    // 401: the caller is not signed in, or the token they sent is no good.
    private static Task WriteUnauthorizedAsync(JwtBearerChallengeContext context)
    {
        // We write the response ourselves, so the default empty 401 is not written as well.
        context.HandleResponse();

        var (code, detail, challenge) = context.AuthenticateFailure switch
        {
            null => (RequiredCode, "A bearer token is required.", "Bearer"),
            SecurityTokenExpiredException => (
                TokenExpiredCode, "The token has expired. Sign in again.", "Bearer error=\"invalid_token\", error_description=\"The token expired\""),
            _ => (InvalidTokenCode, "The token is not valid.", "Bearer error=\"invalid_token\""),
        };

        // The detail is fixed text on purpose: the library's own message names keys and algorithms, which help an attacker.
        context.Response.Headers.WWWAuthenticate = challenge; // RFC 6750: a 401 from a bearer API says how to authenticate
        return WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Authentication is required.", detail, code);
    }

    // 403: the caller is signed in, but is not allowed to do this.
    private static Task WriteForbiddenAsync(ForbiddenContext context) =>
        WriteProblemAsync(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            "Access is denied.",
            "You do not have permission to do this.",
            ForbiddenCode);

    // Authentication runs before MVC, so the controller's error mapping is not involved: build the same application/problem+json
    // response here, through the same service every other error uses (it adds the type and the traceId).
    private static async Task WriteProblemAsync(HttpContext http, int status, string title, string detail, string code)
    {
        http.Response.StatusCode = status;
        await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        });
    }
}
