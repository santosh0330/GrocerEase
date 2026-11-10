using GrocerEase.Application.Authentication.Commands;

namespace GrocerEase.UnitTests.Application;

public class AuthenticationCommandsTests
{
    [Fact]
    public void RegisterCommand_ExposesTheValuesItWasCreatedWith()
    {
        var command = new RegisterCommand("Ada", "Lovelace", "ada@example.com", "s3cret!");

        Assert.Equal(("Ada", "Lovelace", "ada@example.com", "s3cret!"),
            (command.FirstName, command.LastName, command.Email, command.Password));
    }

    [Fact]
    public void LoginCommand_ExposesTheValuesItWasCreatedWith()
    {
        var command = new LoginCommand("ada@example.com", "s3cret!");

        Assert.Equal(("ada@example.com", "s3cret!"), (command.Email, command.Password));
    }

    [Fact]
    public void Commands_AreEqualWhenTheirValuesAreEqual()
    {
        Assert.Equal(new LoginCommand("ada@example.com", "s3cret!"), new LoginCommand("ada@example.com", "s3cret!"));
        Assert.NotEqual(new LoginCommand("ada@example.com", "s3cret!"), new LoginCommand("ada@example.com", "other"));
    }

    [Fact]
    public void RegisterCommand_ToString_DoesNotContainPassword()
    {
        var text = new RegisterCommand("Ada", "Lovelace", "ada@example.com", "s3cret!").ToString();

        Assert.DoesNotContain("s3cret!", text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginCommand_ToString_DoesNotContainPassword() =>
        Assert.DoesNotContain("s3cret!", new LoginCommand("ada@example.com", "s3cret!").ToString(), StringComparison.Ordinal);

    [Fact]
    public void RefreshCommand_ToString_DoesNotContainTheRefreshToken()
    {
        var text = new RefreshCommand("refresh-token-value").ToString();

        Assert.DoesNotContain("refresh-token-value", text, StringComparison.Ordinal);
        Assert.Contains("RefreshToken = ***", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshCommand_IsEqualWhenTheTokensAreEqual()
    {
        Assert.Equal(new RefreshCommand("a"), new RefreshCommand("a"));
        Assert.NotEqual(new RefreshCommand("a"), new RefreshCommand("b"));
    }
}
