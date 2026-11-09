using GrocerEase.Application.Common;

namespace GrocerEase.UnitTests.Application;

public class ResultTests
{
    private static readonly ResultError DuplicateEmail = new("User.DuplicateEmail", "The email is already registered.", ErrorType.Conflict);

    [Fact]
    public void Success_HoldsTheValueAndNoError()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Success_ThenReadingTheError_Throws()
    {
        var result = Result.Success(42);

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_HoldsTheErrorAndNoValue()
    {
        var result = Result.Failure<int>(DuplicateEmail);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(DuplicateEmail, result.Error);
    }

    [Fact]
    public void Failure_ThenReadingTheValue_Throws()
    {
        var result = Result.Failure<int>(DuplicateEmail);

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Success_WithANullValue_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Result.Success<string>(null!));

    [Fact]
    public void Failure_WithANullError_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Result.Failure<int>(null!));

    [Fact]
    public void Match_OnSuccess_RunsOnlyTheSuccessBranch()
    {
        var text = Result.Success(42).Match(value => $"value {value}", error => $"error {error.Code}");

        Assert.Equal("value 42", text);
    }

    [Fact]
    public void Match_OnFailure_RunsOnlyTheFailureBranch()
    {
        var text = Result.Failure<int>(DuplicateEmail).Match(value => $"value {value}", error => $"error {error.Code}");

        Assert.Equal("error User.DuplicateEmail", text);
    }
}
