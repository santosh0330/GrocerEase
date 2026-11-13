using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace GrocerEase.Api.ExceptionHandling;

/// <summary>
/// The 400 that <c>[ApiController]</c> returns when model binding fails (malformed JSON, a missing required property),
/// before the action or any handler runs. It is MVC's default response with the keys renamed by <see cref="ErrorKeys"/>,
/// so a client sees <c>lines</c> whether MVC or a validator caught the problem.
/// </summary>
public static class InvalidModelStateResponse
{
    private static readonly string[] ProblemContentTypes = ["application/problem+json", "application/problem+xml"];

    public static IActionResult Create(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateValidationProblemDetails(context.HttpContext, context.ModelState);

        var rekeyed = ErrorKeys.Rekey(problem.Errors);
        problem.Errors.Clear();
        foreach (var (key, messages) in rekeyed)
        {
            problem.Errors[key] = messages;
        }

        var result = new BadRequestObjectResult(problem);
        foreach (var contentType in ProblemContentTypes)
        {
            result.ContentTypes.Add(contentType);
        }

        return result;
    }
}
