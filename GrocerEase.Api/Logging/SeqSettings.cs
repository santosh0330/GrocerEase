using System.ComponentModel.DataAnnotations;

namespace GrocerEase.Api.Logging;

/// <summary>
/// The <c>Seq</c> section: where to send the logs for searching. An empty <see cref="ServerUrl"/> turns Seq off, and the app logs to
/// the console and the files only. A value that is not a URL stops the app at start-up, instead of silently sending nothing.
/// </summary>
public sealed class SeqSettings : IValidatableObject
{
    public const string SectionName = "Seq";

    /// <summary>The address of the Seq server, for example <c>http://seq:5341</c>. Empty means "do not send".</summary>
    public string ServerUrl { get; init; } = string.Empty;

    /// <summary>
    /// A Seq API key, if the server asks for one. A secret: it belongs in user-secrets or the environment (<c>Seq__ApiKey</c>),
    /// never in a committed file.
    /// </summary>
    public string? ApiKey { get; init; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ServerUrl);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IsEnabled
            && !(Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))))
        {
            yield return new ValidationResult(
                "Seq:ServerUrl must be an absolute http or https URL, or empty to turn Seq off.", [nameof(ServerUrl)]);
        }
    }
}
