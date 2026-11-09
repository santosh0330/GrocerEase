using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GrocerEase.UnitTests.Api;

/// <summary>Builds a plain <see cref="ProblemDetails"/> so a controller can be tested without an HttpContext or DI container.</summary>
internal sealed class StubProblemDetailsFactory : ProblemDetailsFactory
{
    public override ProblemDetails CreateProblemDetails(
        HttpContext httpContext, int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) =>
        new() { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };

    public override ValidationProblemDetails CreateValidationProblemDetails(
        HttpContext httpContext, ModelStateDictionary modelStateDictionary, int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) =>
        new(modelStateDictionary) { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };
}
