using System.Security.Claims;
using GrocerEase.Api.Authentication;
using GrocerEase.Api.Controllers;
using GrocerEase.Contracts.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class MeControllerTests
{
    private static MeController ControllerFor(params Claim[] claims) => new()
    {
        ProblemDetailsFactory = new StubProblemDetailsFactory(),
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) },
        },
    };

    private static Claim[] AdaClaims() =>
    [
        new("sub", "018f3b1c-7a2e-7d11-8c3f-2b6d5e4a9c10"),
        new("given_name", "Ada"),
        new("family_name", "Lovelace"),
        new("email", "ada@example.com"),
        new("role", "Customer"),
    ];

    [Fact]
    public void Get_ReturnsTheUserTheTokenNames()
    {
        var result = ControllerFor(AdaClaims()).Get();

        var body = Assert.IsType<CurrentUserResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(("Ada", "Lovelace", "ada@example.com", "Customer"), (body.FirstName, body.LastName, body.Email, body.Role));
        Assert.Equal(Guid.Parse("018f3b1c-7a2e-7d11-8c3f-2b6d5e4a9c10"), body.Id);
    }

    [Fact]
    public void Get_WhenTheTokenDoesNotIdentifyAUser_Returns401WithAnInvalidTokenProblem_NotAServerError()
    {
        var controller = ControllerFor(new Claim("sub", "018f3b1c-7a2e-7d11-8c3f-2b6d5e4a9c10")); // no name, no email

        var result = Assert.IsType<ObjectResult>(controller.Get());

        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(AuthenticationExtensions.InvalidTokenCode, problem.Extensions["code"]);
        Assert.Equal("Bearer error=\"invalid_token\"", controller.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public void TheController_RequiresAnAuthenticatedUser() =>
        Assert.NotNull(typeof(MeController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).SingleOrDefault());
}
