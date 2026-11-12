using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Application.Stores.Handlers;
using GrocerEase.Application.Stores.Queries;

namespace GrocerEase.UnitTests.Application;

public class StoreHandlersTests
{
    private readonly FakeStoreRepository _stores = new();

    private static readonly CreateStoreCommand FreshMart = new("Fresh Mart", "1 High Street");

    [Fact]
    public async Task Create_StoresTheStore_AndReturnsItWithAVersion7Id()
    {
        var result = await new CreateStoreCommandHandler(_stores).Handle(FreshMart, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(("Fresh Mart", "1 High Street"), (result.Value.Name, result.Value.Address));
        Assert.Equal(7, result.Value.Id.Version);
        Assert.NotNull(await _stores.GetByIdAsync(result.Value.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Create_WhenTheNameIsTaken_ReturnsDuplicateName_AndStoresNothingMore()
    {
        var handler = new CreateStoreCommandHandler(_stores);
        await handler.Handle(FreshMart, CancellationToken.None);

        var result = await handler.Handle(FreshMart with { Address = "2 Other Road" }, CancellationToken.None);

        Assert.Same(StoreErrors.DuplicateName, result.Error);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(await _stores.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Create_TrimsTheName_SoPaddingDoesNotMakeANewStore()
    {
        var handler = new CreateStoreCommandHandler(_stores);
        await handler.Handle(FreshMart, CancellationToken.None);

        var result = await handler.Handle(FreshMart with { Name = "  Fresh Mart " }, CancellationToken.None);

        Assert.Same(StoreErrors.DuplicateName, result.Error);
    }

    [Fact]
    public async Task Get_ReturnsTheStore()
    {
        var created = (await new CreateStoreCommandHandler(_stores).Handle(FreshMart, CancellationToken.None)).Value;

        var result = await new GetStoreQueryHandler(_stores).Handle(new GetStoreQuery(created.Id), CancellationToken.None);

        Assert.Equal(created, result.Value);
    }

    [Fact]
    public async Task Get_AnUnknownStore_ReturnsNotFound()
    {
        var result = await new GetStoreQueryHandler(_stores).Handle(new GetStoreQuery(Guid.CreateVersion7()), CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task List_ReturnsEveryStore_OrderedByName()
    {
        var create = new CreateStoreCommandHandler(_stores);
        await create.Handle(new CreateStoreCommand("Green Basket", "2 Park Lane"), CancellationToken.None);
        await create.Handle(FreshMart, CancellationToken.None);

        var result = await new ListStoresQueryHandler(_stores).Handle(new ListStoresQuery(), CancellationToken.None);

        Assert.Equal(["Fresh Mart", "Green Basket"], result.Value.Select(store => store.Name), StringComparer.Ordinal);
    }

    [Fact]
    public async Task List_WithNoStores_IsASuccessWithAnEmptyList()
    {
        var result = await new ListStoresQueryHandler(_stores).Handle(new ListStoresQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
