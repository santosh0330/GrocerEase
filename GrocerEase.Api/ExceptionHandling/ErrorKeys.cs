using System.Text.Json;

namespace GrocerEase.Api.ExceptionHandling;

/// <summary>
/// The one rule for the keys of a validation problem's <c>errors</c>: the path of the JSON property the client sent,
/// each segment named the way the serializer names it (<c>Lines[0].Quantity</c> becomes <c>lines[0].quantity</c>).
/// MVC's own checks and FluentValidation both report C# property names, so both go through here.
/// </summary>
public static class ErrorKeys
{
    /// <summary>Camel-cases every dot-separated segment of <paramref name="key"/>, leaving indexers and a leading <c>$</c> alone.</summary>
    public static string CamelCase(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return string.Join('.', key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
    }

    /// <summary>Re-keys <paramref name="errors"/> with <see cref="CamelCase"/>; messages of keys that become the same are merged.</summary>
    public static IDictionary<string, string[]> Rekey(IEnumerable<KeyValuePair<string, string[]>> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors
            .GroupBy(error => CamelCase(error.Key), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(error => error.Value).ToArray(),
                StringComparer.Ordinal);
    }
}
