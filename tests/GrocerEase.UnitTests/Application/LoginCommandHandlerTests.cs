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

        Assert.Equal("ada@example.com", result.Email);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.False(string.IsNullOrEmpty(result.Token));
    }
}
