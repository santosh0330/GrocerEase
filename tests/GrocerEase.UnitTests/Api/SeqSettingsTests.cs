using System.ComponentModel.DataAnnotations;
using GrocerEase.Api.Logging;

namespace GrocerEase.UnitTests.Api;

public class SeqSettingsTests
{
    private static List<ValidationResult> Validate(SeqSettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("http://localhost:5341")]
    [InlineData("http://seq:5341")]
    [InlineData("https://seq.example.com")]
    [InlineData("HTTP://SEQ:5341")]
    public void AnAbsoluteHttpOrHttpsUrl_IsAccepted(string url)
    {
        var settings = new SeqSettings { ServerUrl = url };

        Assert.Empty(Validate(settings));
        Assert.True(settings.IsEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyUrl_TurnsSeqOff_AndIsValid(string url)
    {
        var settings = new SeqSettings { ServerUrl = url };

        Assert.Empty(Validate(settings));
        Assert.False(settings.IsEnabled);
    }

    [Theory]
    [InlineData("seq")]
    [InlineData("localhost:5341")]
    [InlineData("/relative/path")]
    [InlineData("ftp://seq:5341")]
    [InlineData("file:///var/log/seq")]
    [InlineData("not a url")]
    public void AValueThatIsNotAnHttpUrl_IsRejected_AndTheMessageNamesTheSetting(string url)
    {
        var error = Assert.Single(Validate(new SeqSettings { ServerUrl = url }));

        Assert.Contains("Seq:ServerUrl", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TheApiKey_IsOptional() => Assert.Empty(Validate(new SeqSettings { ServerUrl = "http://seq:5341", ApiKey = null }));
}
