using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Handlers;

namespace GrocerEase.UnitTests.Application;

public class RegisterCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAResultForTheRegisteredUserWithAToken()
    {
        var handler = new RegisterCommandHandler();

        var result = await handler.Handle(
            new RegisterCommand("Ada", "Lovelace", "ada@example.com", "s3cret!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = result.Value;
        Assert.Equal(("Ada", "Lovelace", "ada@example.com"), (user.FirstName, user.LastName, user.Email));
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.False(string.IsNullOrEmpty(user.Token));
    }
}
