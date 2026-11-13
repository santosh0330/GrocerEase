using FluentValidation;
using FluentValidation.Results;
using GrocerEase.Api.ExceptionHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class ValidationExceptionHandlerTests
{
    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.FromResult(true);
        }
    }

    private readonly RecordingProblemDetailsService _service = new();

    [Fact]
    public async Task ValidationException_BecomesA400WithErrorsKeyedByCamelCasedProperty()
    {
        var context = new DefaultHttpContext();
        var exception = new ValidationException(
        [
            new ValidationFailure("FirstName", "First name is required"),
            new ValidationFailure("FirstName", "First name is too long"),
            new ValidationFailure("Email", "Email is invalid"),
        ]);

        var handled = await new ValidationExceptionHandler(_service).TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(_service.Written?.ProblemDetails);
        Assert.Equal(["First name is required", "First name is too long"], problem.Errors["firstName"]);
        Assert.Equal(["Email is invalid"], problem.Errors["email"]);
    }

    [Fact]
    public async Task NestedProperties_AreKeyedByTheirJsonPath()
    {
        var context = new DefaultHttpContext();
        var exception = new ValidationException(
        [
            new ValidationFailure("Lines[0].Quantity", "Quantity must be positive"),
            new ValidationFailure("Lines[1].ProductId", "Product is required"),
        ]);

        await new ValidationExceptionHandler(_service).TryHandleAsync(context, exception, CancellationToken.None);

        var problem = Assert.IsType<ValidationProblemDetails>(_service.Written?.ProblemDetails);
        Assert.Equal(["lines[0].quantity", "lines[1].productId"], problem.Errors.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    public async Task OtherExceptions_AreLeftForTheNextHandler()
    {
        var context = new DefaultHttpContext();

        var handled = await new ValidationExceptionHandler(_service)
            .TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.False(handled);
        Assert.Null(_service.Written);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }
}
