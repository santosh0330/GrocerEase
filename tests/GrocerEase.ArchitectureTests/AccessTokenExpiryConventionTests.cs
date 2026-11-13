using System.Reflection;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Infrastructure.Authentication;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// When an access token expires has <b>one source</b>: the token generator reads <c>Jwt:ExpiryMinutes</c>, writes the result into the token's
/// <c>exp</c> claim and hands the very same instant back as <see cref="IssuedAccessToken.ExpiresAt"/>, which is what the sign-in response
/// says. If a second place read the setting and worked out an expiry of its own, the response and the token could disagree (by a rounding, a
/// clock read a moment later, or a setting changed in one place only), and a client that trusted the response would refresh too late or too early.
/// </summary>
public class AccessTokenExpiryConventionTests
{
    private static readonly Assembly[] Shipped =
    [
        typeof(Domain.AssemblyReference).Assembly,
        typeof(Application.AssemblyReference).Assembly,
        typeof(Infrastructure.AssemblyReference).Assembly,
        typeof(GrocerEase.Api.Controllers.AuthController).Assembly,
    ];

    // The IL, not the source: a read hidden in a lambda or an async method is found too. The reading of a property is a call to its getter.
    private static List<string> TopLevelTypesThatReadTheLifetime(string assemblyPath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var getter = $"get_{nameof(JwtSettings.ExpiryMinutes)}";

        static TypeDefinition TopLevel(TypeDefinition type) => type.DeclaringType is null ? type : TopLevel(type.DeclaringType);

        return [.. assembly.MainModule.GetTypes()
            .Where(type => type.Methods.Any(method => method.HasBody && method.Body.Instructions.Any(instruction =>
                instruction.OpCode.Code is Code.Call or Code.Callvirt
                && instruction.Operand is MethodReference called
                && string.Equals(called.Name, getter, StringComparison.Ordinal)
                && string.Equals(called.DeclaringType.FullName, typeof(JwtSettings).FullName, StringComparison.Ordinal))))
            .Select(type => TopLevel(type).Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void OnlyTheTokenGenerator_ReadsTheLifetimeSetting()
    {
        var readers = Shipped.SelectMany(assembly => TopLevelTypesThatReadTheLifetime(assembly.Location)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            readers.Count == 1 && string.Equals(readers[0], nameof(JwtTokenGenerator), StringComparison.Ordinal),
            $"Only {nameof(JwtTokenGenerator)} may read Jwt:ExpiryMinutes, so the expiry in a response is the one in the token: {string.Join(", ", readers)}");
    }

    [Fact]
    public void TheTokenGenerator_HandsBackTheExpiryWithTheToken_NotABareString()
    {
        var generate = typeof(IJwtTokenGenerator).GetMethod(nameof(IJwtTokenGenerator.GenerateToken));

        Assert.NotNull(generate);
        Assert.Equal(typeof(IssuedAccessToken), generate.ReturnType); // a bare string would leave a caller to work out the expiry on its own
    }

    private sealed class CarelessReader(JwtSettings settings)
    {
        public DateTimeOffset ExpiryIGuessed() => DateTimeOffset.UtcNow.AddMinutes(settings.ExpiryMinutes);
    }

    [Fact]
    public void TheRule_CanFail_BecauseTheScannerFindsAReaderOfTheSetting_WhereThereIsOne()
    {
        // The test assembly holds a careless reader on purpose, and the shipped one holds the generator: a scanner that saw nothing would pass the rule above for nothing.
        Assert.Equal([nameof(AccessTokenExpiryConventionTests)], TopLevelTypesThatReadTheLifetime(typeof(CarelessReader).Assembly.Location), StringComparer.Ordinal);
        Assert.Contains(nameof(JwtTokenGenerator), TopLevelTypesThatReadTheLifetime(typeof(JwtTokenGenerator).Assembly.Location), StringComparer.Ordinal);
        Assert.NotNull(new CarelessReader(new JwtSettings()));
    }
}
