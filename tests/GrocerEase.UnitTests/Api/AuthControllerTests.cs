using GrocerEase.Api.Controllers;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.UnitTests.Api;

public class AuthControllerTests
{
    private readonly AuthController _controller = new();

    [Fact]
    public void Register_ReturnsOkWithTheRegisteredUser()
    {
        var result = _controller.Register(new RegisterRequest("Ada", "Lovelace", "ada@example.com", "s3cret!"));

        var response = Assert.IsType<AuthenticationResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(("Ada", "Lovelace", "ada@example.com"), (response.FirstName, response.LastName, response.Email));
        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.False(string.IsNullOrEmpty(response.Token));
    }

    [Fact]
    public void Login_ReturnsOkWithTheEmailAndAToken()
    {
        var result = _controller.Login(new LoginRequest("ada@example.com", "s3cret!"));

        var response = Assert.IsType<AuthenticationResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("ada@example.com", response.Email);
        Assert.False(string.IsNullOrEmpty(response.Token));
    }
}
