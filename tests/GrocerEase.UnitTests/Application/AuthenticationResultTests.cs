using GrocerEase.Application.Authentication.Common;

namespace GrocerEase.UnitTests.Application;

public class AuthenticationResultTests
{
    [Fact]
    public void ExposesTheValuesItWasCreatedWith()
    {
        var id = Guid.NewGuid();

        var expiresAt = new DateTimeOffset(2030, 1, 1, 12, 10, 0, TimeSpan.Zero);

        var result = new AuthenticationResult(id, "Ada", "Lovelace", "ada@example.com", "jwt", expiresAt, "refresh");

        Assert.Equal((id, "Ada", "Lovelace", "ada@example.com", "jwt", expiresAt, "refresh"),
            (result.Id, result.FirstName, result.LastName, result.Email, result.Token, result.ExpiresAt, result.RefreshToken));
    }

    [Fact]
    public void ToString_DoesNotContainEitherToken()
    {
        var text = new AuthenticationResult(Guid.NewGuid(), "Ada", "Lovelace", "ada@example.com", "jwt-value", new DateTimeOffset(2030, 1, 1, 12, 10, 0, TimeSpan.Zero), "refresh-value").ToString();

        Assert.DoesNotContain("jwt-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-value", text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", text, StringComparison.Ordinal);
        Assert.Contains("2030-01-01T12:10:00", text, StringComparison.Ordinal); // when it expires is not a secret
    }
}
