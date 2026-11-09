using System.Security.Claims;
using GrocerEase.Api.Mapping;

namespace GrocerEase.UnitTests.Api;

public class CurrentUserMappingsTests
{
    private static readonly Guid UserId = Guid.Parse("018f3b1c-7a2e-7d11-8c3f-2b6d5e4a9c10");

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), authenticationType: "Bearer"));

    private static (string, string)[] AllClaims() =>
    [
        ("sub", UserId.ToString()),
        ("given_name", "Ada"),
        ("family_name", "Lovelace"),
        ("email", "ada@example.com"),
    ];

    [Fact]
    public void TheClaimsOfAToken_BecomeTheCurrentUser()
    {
        var response = Principal(AllClaims()).ToCurrentUserResponse();

        Assert.NotNull(response);
        Assert.Equal((UserId, "Ada", "Lovelace", "ada@example.com"), (response.Id, response.FirstName, response.LastName, response.Email));
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("given_name")]
    [InlineData("family_name")]
    [InlineData("email")]
    public void AMissingClaim_MeansTheTokenDoesNotIdentifyAUser(string missing)
    {
        var claims = AllClaims().Where(c => !string.Equals(c.Item1, missing, StringComparison.Ordinal)).ToArray();

        Assert.Null(Principal(claims).ToCurrentUserResponse());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void ASubjectThatIsNotAnId_MeansTheTokenDoesNotIdentifyAUser(string subject)
    {
        var claims = AllClaims().Select(c => string.Equals(c.Item1, "sub", StringComparison.Ordinal) ? (c.Item1, subject) : c).ToArray();

        Assert.Null(Principal(claims).ToCurrentUserResponse());
    }

    [Fact]
    public void ABlankClaim_MeansTheTokenDoesNotIdentifyAUser()
    {
        var claims = AllClaims().Select(c => string.Equals(c.Item1, "email", StringComparison.Ordinal) ? (c.Item1, " ") : c).ToArray();

        Assert.Null(Principal(claims).ToCurrentUserResponse());
    }

    [Fact]
    public void LongDotNetClaimNames_AreNotUsed_BecauseTheTokenCarriesTheShortOnes()
    {
        // With claim mapping off, "sub" stays "sub". A principal that only has the mapped URI would not be ours.
        var principal = Principal(
            (ClaimTypes.NameIdentifier, UserId.ToString()), ("given_name", "Ada"), ("family_name", "Lovelace"), ("email", "ada@example.com"));

        Assert.Null(principal.ToCurrentUserResponse());
    }

    [Fact]
    public void RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => CurrentUserMappings.ToCurrentUserResponse(null!));
}
