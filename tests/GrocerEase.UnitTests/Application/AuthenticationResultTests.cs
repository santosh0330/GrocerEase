using GrocerEase.Application.Authentication.Common;

namespace GrocerEase.UnitTests.Application;

public class AuthenticationResultTests
{
    [Fact]
    public void ExposesTheValuesItWasCreatedWith()
    {
        var id = Guid.NewGuid();

        var result = new AuthenticationResult(id, "Ada", "Lovelace", "ada@example.com", "jwt", "refresh");

        Assert.Equal((id, "Ada", "Lovelace", "ada@example.com", "jwt", "refresh"),
            (result.Id, result.FirstName, result.LastName, result.Email, result.Token, result.RefreshToken));
    }

    [Fact]
    public void ToString_DoesNotContainEitherToken()
    {
        var text = new AuthenticationResult(Guid.NewGuid(), "Ada", "Lovelace", "ada@example.com", "jwt-value", "refresh-value").ToString();

        Assert.DoesNotContain("jwt-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-value", text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", text, StringComparison.Ordinal);
    }
}
