using GrocerEase.Api.ExceptionHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.UnitTests.Api;

public class InvalidModelStateResponseTests
{
    private static ActionContext ContextWith(ModelStateDictionary modelState)
    {
        var services = new ServiceCollection().AddSingleton<ProblemDetailsFactory, StubProblemDetailsFactory>().BuildServiceProvider();
        return new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(), new ActionDescriptor(), modelState);
    }

    [Fact]
    public void AMissingRequiredProperty_IsKeyedTheWayTheClientWroteIt()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Lines", "The Lines field is required.");
        modelState.AddModelError("Lines[0].Quantity", "Quantity must be positive.");
        modelState.AddModelError("$.email", "The JSON value could not be converted.");

        var result = Assert.IsType<BadRequestObjectResult>(InvalidModelStateResponse.Create(ContextWith(modelState)));

        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Equal(["$.email", "lines", "lines[0].quantity"], problem.Errors.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(["The Lines field is required."], problem.Errors["lines"]);
    }

    [Fact]
    public void TheResponse_IsProblemJson()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Lines", "required");

        var result = Assert.IsType<BadRequestObjectResult>(InvalidModelStateResponse.Create(ContextWith(modelState)));

        Assert.Contains("application/problem+json", result.ContentTypes, StringComparer.Ordinal);
    }
}
