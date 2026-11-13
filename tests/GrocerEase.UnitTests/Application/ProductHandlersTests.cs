using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Commands;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Products.Handlers;
using GrocerEase.Application.Products.Queries;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class ProductHandlersTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeStoreMemberRepository _members = new();
    private readonly Guid _storeId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();

    public ProductHandlersTests() =>
        _members.Members.Add(new StoreMember(Guid.CreateVersion7(), _storeId, _userId)); // the user runs the store

    private CreateProductCommandHandler Create() => new(new StoreAccess(_members), _products);

    private CreateProductCommand Milk(string name = "Milk") => new(_storeId, _userId, name, "Fresh milk", 2.50m);

    [Fact]
    public async Task AMember_AddsAProduct_ToTheirStore()
    {
        var result = await Create().Handle(Milk(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((_storeId, "Milk", "Fresh milk", 2.50m), (result.Value.StoreId, result.Value.Name, result.Value.Description, result.Value.Price));
        Assert.Equal(7, result.Value.Id.Version);
        Assert.Single(_products.Products);
    }

    [Fact]
    public async Task ANonMember_IsToldTheStoreIsNotFound_AndNothingIsAdded()
    {
        var stranger = Milk() with { ActorId = Guid.CreateVersion7() };

        var result = await Create().Handle(stranger, CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
        Assert.Empty(_products.Products);
    }

    [Fact]
    public async Task AMemberOfAnotherStore_IsToldTheStoreIsNotFound()
    {
        var elsewhere = Milk() with { StoreId = Guid.CreateVersion7() };

        var result = await Create().Handle(elsewhere, CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task TheSameNameTwiceInOneStore_IsADuplicate_ButTheNameFreedByArchivingMayBeUsed()
    {
        await Create().Handle(Milk(), CancellationToken.None);

        var duplicate = await Create().Handle(Milk(), CancellationToken.None);

        Assert.Same(ProductErrors.DuplicateName, duplicate.Error);
        Assert.Equal(ErrorType.Conflict, duplicate.Error.Type);

        _products.Products.Clear();
        _products.Products.Add(new Product(Guid.CreateVersion7(), _storeId, "Milk", "", 1m, DateTimeOffset.UtcNow)); // archived
        Assert.True((await Create().Handle(Milk(), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task TheOwnershipRule_IsCheckedBeforeAnythingElse_SoAStrangerCannotLearnWhichNamesAreTaken()
    {
        await Create().Handle(Milk(), CancellationToken.None);
        var stranger = Milk() with { ActorId = Guid.CreateVersion7() };

        var result = await Create().Handle(stranger, CancellationToken.None); // the name is taken, but they must not be told

        Assert.Same(StoreErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Get_ReturnsAProductThatIsOnSale_InItsStore()
    {
        var created = (await Create().Handle(Milk(), CancellationToken.None)).Value;

        var result = await new GetProductQueryHandler(_products).Handle(new GetProductQuery(_storeId, created.Id), CancellationToken.None);

        Assert.Equal(created, result.Value);
    }

    [Fact]
    public async Task Get_AnUnknownProduct_AnArchivedOne_AndAnotherStoresOne_AreAllNotFound()
    {
        var created = (await Create().Handle(Milk(), CancellationToken.None)).Value;
        var archived = new Product(Guid.CreateVersion7(), _storeId, "Old", "", 1m, DateTimeOffset.UtcNow);
        _products.Products.Add(archived);
        var handler = new GetProductQueryHandler(_products);

        var unknown = await handler.Handle(new GetProductQuery(_storeId, Guid.CreateVersion7()), CancellationToken.None);
        var archivedResult = await handler.Handle(new GetProductQuery(_storeId, archived.Id), CancellationToken.None);
        var otherStore = await handler.Handle(new GetProductQuery(Guid.CreateVersion7(), created.Id), CancellationToken.None);

        Assert.Same(ProductErrors.NotFound, unknown.Error);
        Assert.Same(ProductErrors.NotFound, archivedResult.Error);
        Assert.Same(ProductErrors.NotFound, otherStore.Error);
    }
}
