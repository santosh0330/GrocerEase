using FluentValidation;
using GrocerEase.Application;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Decorators;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.UnitTests.Application;

public class TransactionDecoratorTests
{
    private static readonly ResultError Refused = new("Test.Refused", "The handler said no.", ErrorType.Conflict);

    private sealed record DoCommand(string Name);

    private sealed class DoCommandValidator : AbstractValidator<DoCommand>
    {
        public DoCommandValidator() => RuleFor(command => command.Name).NotEmpty();
    }

    // What the handler does is written into the same diary as the unit's, so the order of the whole story can be asserted.
    private sealed class DiaryHandler(FakeUnitOfWork unitOfWork, Func<Result<string>> outcome) : ICommandHandler<DoCommand, Result<string>>
    {
        public Task<Result<string>> Handle(DoCommand command, CancellationToken cancellationToken)
        {
            unitOfWork.Events.Add($"handle {command.Name}");
            return Task.FromResult(outcome());
        }
    }

    private static TransactionDecorator<DoCommand, Result<string>> Decorate(FakeUnitOfWork unitOfWork, Func<Result<string>> outcome) =>
        new(new DiaryHandler(unitOfWork, outcome), unitOfWork);

    [Fact]
    public async Task ASuccess_IsCommitted_AfterTheHandlerHasRun()
    {
        var unitOfWork = new FakeUnitOfWork();

        var result = await Decorate(unitOfWork, () => Result.Success("done")).Handle(new DoCommand("a"), CancellationToken.None);

        Assert.Equal("done", result.Value);
        Assert.Equal(["begin", "handle a", "commit"], unitOfWork.Events);
    }

    [Fact]
    public async Task AFailureResult_IsRolledBack_AndReturnedUnchanged()
    {
        var unitOfWork = new FakeUnitOfWork();

        var result = await Decorate(unitOfWork, () => Result.Failure<string>(Refused)).Handle(new DoCommand("a"), CancellationToken.None);

        Assert.Same(Refused, result.Error);
        Assert.Equal(["begin", "handle a", "rollback"], unitOfWork.Events);
    }

    [Fact]
    public async Task AnException_IsRolledBack_AndStillThrown()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = Decorate(unitOfWork, () => throw new InvalidOperationException("boom"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => decorator.Handle(new DoCommand("a"), CancellationToken.None));

        Assert.Equal("boom", exception.Message);
        Assert.Equal(["begin", "handle a", "rollback"], unitOfWork.Events);
    }

    [Fact]
    public async Task TheUnitIsBegunBeforeTheHandlerRuns_SoEveryWriteOfTheHandlerIsInsideIt()
    {
        var unitOfWork = new FakeUnitOfWork();

        await Decorate(unitOfWork, () => Result.Success("x")).Handle(new DoCommand("a"), CancellationToken.None);

        Assert.True(unitOfWork.Events.IndexOf("begin") < unitOfWork.Events.IndexOf("handle a"));
    }

    [Fact]
    public async Task ANullCommand_IsRefused_BeforeAnyUnitIsBegun()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = Decorate(unitOfWork, () => Result.Success("x"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => decorator.Handle(null!, CancellationToken.None));

        Assert.Empty(unitOfWork.Events);
    }

    // The registration helper: validation is the outer wrapper, the transaction the inner one.
    private static ServiceProvider Build(FakeUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton<Func<Result<string>>>(() => Result.Success("ok"));
        services.AddScoped<IValidator<DoCommand>, DoCommandValidator>();
        services.AddTransactionalCommandHandler<DoCommand, Result<string>, DiaryHandler>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task TheHelper_RunsAValidCommandInsideAUnit()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var provider = Build(unitOfWork);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<DoCommand, Result<string>>>()
            .Handle(new DoCommand("a"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["begin", "handle a", "commit"], unitOfWork.Events);
    }

    [Fact]
    public async Task TheHelper_RefusesAnInvalidCommand_BeforeAnyUnitIsBegun()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var provider = Build(unitOfWork);
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DoCommand, Result<string>>>();

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new DoCommand(string.Empty), CancellationToken.None));

        Assert.Empty(unitOfWork.Events); // no transaction was opened for a request that was going to be refused
    }

    [Fact]
    public void TheHelper_WrapsTheValidationAroundTheTransaction()
    {
        using var provider = Build(new FakeUnitOfWork());
        using var scope = provider.CreateScope();

        Assert.IsType<ValidationDecorator<DoCommand, Result<string>>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<DoCommand, Result<string>>>());
    }

    [Fact]
    public void TheHelper_ReturnsTheSameCollection_SoCallsCanBeChained()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddTransactionalCommandHandler<DoCommand, Result<string>, DiaryHandler>());
    }
}
