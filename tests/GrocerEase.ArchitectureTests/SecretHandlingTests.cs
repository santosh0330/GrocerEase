using System.Reflection;
using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// Where passwords and tokens may and may not go. A secret that reaches a log line, a URL or the database in the clear is
/// compromised for good, and each of these mistakes is one forgotten line away, so the build checks them.
/// </summary>
public class SecretHandlingTests
{
    private static readonly Assembly Application = typeof(Application.AssemblyReference).Assembly;
    private static readonly Assembly Contracts = typeof(Contracts.AssemblyReference).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly string[] SecretWords = ["Password", "Token", "Secret"];

    private static bool LooksSecret(string name) => SecretWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$") is not null;

    /// <summary>A record's generated <c>ToString</c> prints every property. This is the list of those that would print a secret.</summary>
    private static List<string> RecordsThatPrintTheirSecrets(IEnumerable<Type> types)
    {
        var offenders = new List<string>();
        foreach (var type in types.Where(IsRecord))
        {
            var constructor = type.GetConstructors().Single();
            var parameters = constructor.GetParameters();
            var secrets = parameters.Where(p => p.ParameterType == typeof(string) && LooksSecret(p.Name!)).ToList();
            if (secrets.Count == 0)
            {
                continue;
            }

            // A distinctive value for every string, so a secret that appears in the text is recognised by its own value.
            var arguments = parameters
                .Select(p => p.ParameterType == typeof(string) ? (object)$"value-of-{p.Name}-canary" : Activator.CreateInstance(p.ParameterType)!)
                .ToArray();
            var text = constructor.Invoke(arguments).ToString()!;
            offenders.AddRange(secrets
                .Where(secret => text.Contains($"value-of-{secret.Name}-canary", StringComparison.Ordinal))
                .Select(secret => $"{type.Name}.{secret.Name}"));
        }

        return offenders;
    }

    private sealed record LeakyRecord(string Email, string RefreshToken);

    private sealed record MaskedRecord(string Email, string RefreshToken)
    {
        private bool PrintMembers(System.Text.StringBuilder builder)
        {
            builder.Append("Email = ").Append(Email).Append(", RefreshToken = ").Append(RefreshToken is null ? "null" : "***");
            return true;
        }
    }

    [Fact]
    public void Application_AndContracts_Records_NeverPrintAPasswordOrATokenInToString()
    {
        var types = Application.GetTypes().Concat(Contracts.GetTypes()).Where(IsRecord).ToList();
        // Real secret holders exist, or this test checks nothing.
        Assert.Contains(types, type => string.Equals(type.Name, "RefreshCommand", StringComparison.Ordinal));
        Assert.Contains(types, type => string.Equals(type.Name, "AuthenticationResponse", StringComparison.Ordinal));

        var offenders = RecordsThatPrintTheirSecrets(types);

        Assert.True(
            offenders.Count == 0,
            $"A record's ToString prints every property. Override PrintMembers to mask: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheSecretRule_CanFail_BecauseARecordThatPrintsItsTokenIsCaught()
    {
        // Guards the test above against passing by accident (a wrong word list or a wrong record check would find nothing).
        Assert.Equal(["LeakyRecord.RefreshToken"], RecordsThatPrintTheirSecrets([typeof(LeakyRecord)]));
        Assert.Empty(RecordsThatPrintTheirSecrets([typeof(MaskedRecord)]));
    }

    [Fact]
    public void TheRefreshTokenStore_IsGivenHashes_NeverTheTokenText()
    {
        // Every method of the port takes a hash (byte[]), a token entity or an id. A string parameter would be the token itself on
        // its way to be stored or compared in the clear.
        var methods = typeof(IRefreshTokenRepository).GetMethods();
        Assert.NotEmpty(methods);

        var offenders = methods
            .SelectMany(method => method.GetParameters().Where(p => p.ParameterType == typeof(string)).Select(p => $"{method.Name}({p.Name})"))
            .ToList();

        Assert.True(offenders.Count == 0, $"IRefreshTokenRepository must not take token text: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheRefreshTokenEntity_HasNoPropertyThatCouldHoldTheTokenText()
    {
        var textProperties = typeof(RefreshToken).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => p.Name).ToList();

        Assert.True(textProperties.Count == 0, $"RefreshToken stores only the hash, but has text properties: {string.Join(", ", textProperties)}");
    }

    [Fact]
    public void Controllers_NeverTakeAPasswordOrATokenFromTheUrl()
    {
        // A URL ends up in access logs, proxies and browser history. In an [ApiController] a simple-typed parameter (string, Guid,
        // number) is bound from the route or the query string, so a secret must arrive in a request body, as a record.
        var actions = Api.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.IsDefined(typeof(HttpMethodAttribute), inherit: true))
            .ToList();
        Assert.NotEmpty(actions);

        var offenders = actions
            .SelectMany(method => method.GetParameters()
                .Where(p => LooksSecret(p.Name!) && !p.IsDefined(typeof(FromBodyAttribute), inherit: true)
                    && (p.ParameterType.IsPrimitive || p.ParameterType == typeof(string) || p.ParameterType == typeof(Guid)))
                .Select(p => $"{method.DeclaringType!.Name}.{method.Name}({p.Name})"))
            .ToList();

        Assert.True(offenders.Count == 0, $"Secrets must travel in the request body, not the URL: {string.Join(", ", offenders)}");
    }
}
