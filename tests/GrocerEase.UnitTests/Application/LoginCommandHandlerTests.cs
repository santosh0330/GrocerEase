using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Handlers;

namespace GrocerEase.UnitTests.Application;

public class LoginCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAResultForTheEmailWithAToken()
    {
        var handler = new LoginCommandHandler();

        var result = await handler.Handle(new LoginCommand("ada@example.com", "s3cret!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = result.Value;
        Assert.Equal("ada@example.com", user.Email);
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.False(string.IsNullOrEmpty(user.Token));
    }
}
