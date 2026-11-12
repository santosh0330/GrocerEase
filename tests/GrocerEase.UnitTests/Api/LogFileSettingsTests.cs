using System.ComponentModel.DataAnnotations;
using GrocerEase.Api.Logging;

namespace GrocerEase.UnitTests.Api;

public class LogFileSettingsTests
{
    private static List<ValidationResult> Validate(LogFileSettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results;
    }

    private static LogFileSettings Settings(string directory = "logs", int retainedDays = 14, int sizeKilobytes = 102_400) =>
        new() { Directory = directory, RetainedDays = retainedDays, MaxFileSizeKilobytes = sizeKilobytes };

    [Fact]
    public void TheDefaults_AreValid() => Assert.Empty(Validate(Settings()));

    [Fact]
    public void FilesAreOnUnlessTheyAreTurnedOff() => Assert.True(new LogFileSettings().Enabled);

    [Fact]
    public void TurningFilesOff_DoesNotExcuseABadValue_SoSwitchingThemBackOnNeverFailsLater() =>
        Assert.Single(Validate(new LogFileSettings { Enabled = false, Directory = "logs", RetainedDays = 0, MaxFileSizeKilobytes = 102_400 }));

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public void TheEdgesOfTheRetention_AreAccepted(int days) => Assert.Empty(Validate(Settings(retainedDays: days)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void ARetentionOutsideOneToThreeHundredSixtyFiveDays_IsRejected_AndTheMessageNamesTheSetting(int days)
    {
        var error = Assert.Single(Validate(Settings(retainedDays: days)));

        Assert.Contains("LogFiles:RetainedDays", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(1_048_576)]
    public void TheEdgesOfTheFileSize_AreAccepted(int kilobytes) => Assert.Empty(Validate(Settings(sizeKilobytes: kilobytes)));

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(1_048_577)]
    public void AFileSizeOutsideSixtyFourKilobytesToOneGibibyte_IsRejected_AndTheMessageNamesTheSetting(int kilobytes)
    {
        var error = Assert.Single(Validate(Settings(sizeKilobytes: kilobytes)));

        Assert.Contains("LogFiles:MaxFileSizeKilobytes", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingDirectory_IsRejected()
    {
        var error = Assert.Single(Validate(Settings(directory: "")));

        Assert.Contains("LogFiles:Directory", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ASectionThatWasNotConfigured_IsRejected() => Assert.NotEmpty(Validate(new LogFileSettings()));
}
