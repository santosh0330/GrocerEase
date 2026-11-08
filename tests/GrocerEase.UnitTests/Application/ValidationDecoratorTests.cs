using FluentValidation;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Decorators;

namespace GrocerEase.UnitTests.Application;

public class ValidationDecoratorTests
{
    private sealed record TestCommand(string Name);

    private sealed class NameRequired : AbstractValidator<TestCommand>
    {
        public NameRequired() => RuleFor(x => x.Name).NotEmpty();
    }

    private sealed class NameStartsWithA : AbstractValidator<TestCommand>
    {
        public NameStartsWithA() => RuleFor(x => x.Name).Must(name => name.StartsWith('A'));
    }

    private sealed class SpyHandler : ICommandHandler<TestCommand, string>
    {
        public int Calls { get; private set; }

        public Task<string> Handle(TestCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult($"handled {command.Name}");
        }
    }

    private readonly SpyHandler _inner = new();

    private ValidationDecorator<TestCommand, string> Create(params IValidator<TestCommand>[] validators) =>
        new(_inner, validators);

    [Fact]
    public async Task ValidCommand_IsPassedToTheInnerHandlerAndItsResultReturned()
    {
        var result = await Create(new NameRequired()).Handle(new TestCommand("Ada"), CancellationToken.None);

        Assert.Equal("handled Ada", result);
        Assert.Equal(1, _inner.Calls);
    }

    [Fact]
    public async Task InvalidCommand_ThrowsValidationExceptionAndSkipsTheInnerHandler()
    {
        var decorator = Create(new NameRequired());

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => decorator.Handle(new TestCommand(""), CancellationToken.None));

        Assert.Equal("Name", Assert.Single(exception.Errors).PropertyName);
        Assert.Equal(0, _inner.Calls);
    }

    [Fact]
    public async Task FailuresFromAllValidatorsAreCollected()
    {
        // "" fails NameRequired (empty) and NameStartsWithA (does not start with A).
        var decorator = Create(new NameRequired(), new NameStartsWithA());

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => decorator.Handle(new TestCommand(""), CancellationToken.None));

        Assert.Equal(2, exception.Errors.Count());
    }

    [Fact]
    public async Task NoValidators_PassesStraightThrough()
    {
        var result = await Create().Handle(new TestCommand(""), CancellationToken.None);

        Assert.Equal("handled ", result);
    }
}
