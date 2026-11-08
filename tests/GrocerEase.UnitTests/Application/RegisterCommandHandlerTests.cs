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

        Assert.Equal(("Ada", "Lovelace", "ada@example.com"), (result.FirstName, result.LastName, result.Email));
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.False(string.IsNullOrEmpty(result.Token));
    }
}
