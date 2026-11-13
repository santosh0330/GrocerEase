using GrocerEase.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.Api.Mapping;

/// <summary>Application knows what went wrong, not HTTP. Api decides which status each <see cref="ErrorType"/> becomes.</summary>
public static class ErrorMappings
{
    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No HTTP status is mapped for this error type."),
    };

    /// <summary>
    /// Builds an <c>application/problem+json</c> response through the controller's <see cref="ProblemDetailsFactory"/>
    /// (so it carries the usual type and traceId), with the error code as a <c>code</c> field clients can switch on.
    /// </summary>
    public static ObjectResult ToProblem(this ControllerBase controller, ResultError error)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(error);

        var result = controller.Problem(detail: error.Description, statusCode: error.Type.ToStatusCode());
        if (result.Value is ProblemDetails problem)
        {
            problem.Extensions["code"] = error.Code;
        }

        return result;
    }

    /// <summary>
    /// Like <see cref="ToProblem(ControllerBase, ResultError)"/>, and when the error carries data (a change refused because the thing had
    /// moved on, with the thing as it is now), <paramref name="shape"/> turns it into the public type and the problem gets it as
    /// <paramref name="extensionName"/>. Application's types do not reach the client: the controller says what they look like there.
    /// </summary>
    public static ObjectResult ToProblem(this ControllerBase controller, ResultError error, string extensionName, Func<object, object?> shape)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionName);
        ArgumentNullException.ThrowIfNull(shape);

        var result = controller.ToProblem(error);
        if (error.Data is not null && result.Value is ProblemDetails problem && shape(error.Data) is { } shaped)
        {
            problem.Extensions[extensionName] = shaped;
        }

        return result;
    }
}
