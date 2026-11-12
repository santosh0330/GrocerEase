using GrocerEase.Application;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The decorator, the unit of work and the repositories wired together by the real <c>AddApplication</c> and
/// <c>AddInfrastructure</c>, against a real PostgreSQL. This is the proof that a handler that makes several writes is all or
/// nothing: it registers a user through one repository and a refresh token through another, then ends in each possible way.
/// What is left is read on a separate connection.
/// </summary>
public class TransactionalHandlerTests
{
    private static readonly ResultError Refused = new("Test.Refused", "The second write cannot be done.", ErrorType.Conflict);

    private enum Ending
    {
        Success,
        Failure,
        Exception,
    }

    private sealed record WriteTwiceCommand(Guid UserId, string Email, Ending Ending);

    // Two writes through two repositories, then the ending the test asked for.
    private sealed class WriteTwiceHandler(IUserRepository users, IRefreshTokenRepository tokens)
        : ICommandHandler<WriteTwiceCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(WriteTwiceCommand command, CancellationToken cancellationToken)
        {
            var user = new User(command.UserId, "Ada", "Lovelace", command.Email, "a-password-hash", RoleIds.Customer);
            Assert.True(await users.TryAddAsync(user, cancellationToken));
            await tokens.AddAsync(
                RefreshToken.Start(
                    user.Id,
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(RefreshToken.HashLength),
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromDays(3),
                    TimeSpan.FromDays(30)),
                cancellationToken);

            return command.Ending switch
            {
                Ending.Success => Result.Success(user.Id),
                Ending.Failure => Result.Failure<Guid>(Refused),
                _ => throw new InvalidOperationException("the handler broke after both writes"),
            };
        }
    }

    private static async Task<ServiceProvider> BuildAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ConnectionStrings:Default"] = await PostgresServer.ApplicationConnectionStringAsync(),
                ["Jwt:SecretKey"] = ApiFactory.JwtSecretKey,
                ["Jwt:Issuer"] = "GrocerEase",
                ["Jwt:Audience"] = "GrocerEase",
                ["Jwt:ExpiryMinutes"] = "10",
                ["RefreshToken:LifetimeDays"] = "3",
                ["RefreshToken:MaxLifetimeDays"] = "30",
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure(configuration)
            .AddTransactionalCommandHandler<WriteTwiceCommand, Result<Guid>, WriteTwiceHandler>()
            .BuildServiceProvider(validateScopes: true);
    }

    private static async Task<(long Users, long Tokens)> RowsAsync(Guid userId)
    {
        var connection = await PostgresServer.ApplicationConnectionStringAsync();
        return (
            await PostgresServer.ScalarAsync<long>(connection, $"select count(*) from users where id = '{userId}'"),
            await PostgresServer.ScalarAsync<long>(connection, $"select count(*) from refresh_tokens where user_id = '{userId}'"));
    }

    private static async Task<Result<Guid>> RunAsync(Ending ending, Guid userId)
    {
        await using var provider = await BuildAsync();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<WriteTwiceCommand, Result<Guid>>>();

        return await handler.Handle(new WriteTwiceCommand(userId, $"ada-{Guid.NewGuid():N}@example.com", ending), CancellationToken.None);
    }

    [Fact]
    public async Task ASuccess_KeepsBothWrites()
    {
        var userId = Guid.CreateVersion7();

        var result = await RunAsync(Ending.Success, userId);

        Assert.True(result.IsSuccess);
        Assert.Equal((1L, 1L), await RowsAsync(userId));
    }

    [Fact]
    public async Task AFailureResult_AfterBothWrites_LeavesNothingBehind()
    {
        var userId = Guid.CreateVersion7();

        var result = await RunAsync(Ending.Failure, userId);

        Assert.Same(Refused, result.Error);
        Assert.Equal((0L, 0L), await RowsAsync(userId));
    }

    [Fact]
    public async Task AnException_AfterBothWrites_LeavesNothingBehind_AndReachesTheCaller()
    {
        var userId = Guid.CreateVersion7();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(Ending.Exception, userId));

        Assert.Equal((0L, 0L), await RowsAsync(userId));
    }
}
