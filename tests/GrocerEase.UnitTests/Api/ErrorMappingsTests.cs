using GrocerEase.Api.Mapping;
using GrocerEase.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class ErrorMappingsTests
{
    private sealed class TestController : ControllerBase;

    [Theory]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    public void ToStatusCode_MapsEachErrorTypeToItsHttpStatus(ErrorType type, int expected) =>
        Assert.Equal(expected, type.ToStatusCode());

    [Fact]
    public void ToStatusCode_ForAnUnknownErrorType_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ((ErrorType)999).ToStatusCode());

    [Fact]
    public void ToProblem_ReturnsAProblemWithTheStatusDetailAndErrorCode()
    {
        var controller = new TestController { ProblemDetailsFactory = new StubProblemDetailsFactory() };
        var error = new ResultError("User.DuplicateEmail", "The email is already registered.", ErrorType.Conflict);

        var result = controller.ToProblem(error);

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("The email is already registered.", problem.Detail);
        Assert.Equal("User.DuplicateEmail", problem.Extensions["code"]);
    }
}
