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

    private async Task AddStoreAsync(FakeStoreRepository stores) =>
        Assert.True(await stores.TryAddAsync(new Store(_storeId, "Fresh Mart", "1 High Street"), CancellationToken.None));

    [Fact]
    public async Task List_ReturnsOnePage_OrderedByName_WithTheTotal()
    {
        var stores = new FakeStoreRepository();
        await AddStoreAsync(stores);
        foreach (var name in new[] { "Eggs", "Bread", "Milk", "Apples", "Cheese" })
        {
            await Create().Handle(Milk(name), CancellationToken.None);
        }

        var second = await new ListProductsQueryHandler(stores, _products).Handle(new ListProductsQuery(_storeId, 2, 2), CancellationToken.None);

        Assert.Equal(["Cheese", "Eggs"], second.Value.Items.Select(product => product.Name), StringComparer.Ordinal);
        Assert.Equal((2, 2, 5), (second.Value.Page, second.Value.PageSize, second.Value.TotalCount));
    }

    [Fact]
    public async Task List_LeavesOutArchivedProducts_AndAnotherStoresProducts()
    {
        var stores = new FakeStoreRepository();
        await AddStoreAsync(stores);
        await Create().Handle(Milk("Milk"), CancellationToken.None);
        _products.Products.Add(new Product(Guid.CreateVersion7(), _storeId, "Archived", "", 1m, DateTimeOffset.UtcNow));
        _products.Products.Add(new Product(Guid.CreateVersion7(), Guid.CreateVersion7(), "Elsewhere", "", 1m));

        var result = await new ListProductsQueryHandler(stores, _products).Handle(new ListProductsQuery(_storeId, 1, 20), CancellationToken.None);

        Assert.Equal(["Milk"], result.Value.Items.Select(product => product.Name), StringComparer.Ordinal);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task List_APagePastTheEnd_IsEmpty_NotAnError_AndStillSaysHowLongTheListIs()
    {
        var stores = new FakeStoreRepository();
        await AddStoreAsync(stores);
        await Create().Handle(Milk(), CancellationToken.None);

        var result = await new ListProductsQueryHandler(stores, _products).Handle(new ListProductsQuery(_storeId, 9, 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task List_AnUnknownStore_IsNotFound()
    {
        var result = await new ListProductsQueryHandler(new FakeStoreRepository(), _products).Handle(new ListProductsQuery(Guid.CreateVersion7(), 1, 20), CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
    }

    private UpdateProductCommandHandler Update() => new(new StoreAccess(_members), _products);

    private async Task<ProductResult> AddedAsync(string name = "Milk") => (await Create().Handle(Milk(name), CancellationToken.None)).Value;

    [Fact]
    public async Task Update_ChangesTheNameTheDescriptionAndThePrice_AndKeepsTheIdAndTheStore()
    {
        var milk = await AddedAsync();

        var result = await Update().Handle(new UpdateProductCommand(_storeId, milk.Id, _userId, "Oat milk", "Vegan", 3.20m), CancellationToken.None);

        Assert.Equal(new ProductResult(milk.Id, _storeId, "Oat milk", "Vegan", 3.20m, 2), result.Value); // an edit is the next version
        Assert.Equal("Oat milk", Assert.Single(_products.Products).Name);
    }

    [Fact]
    public async Task Update_ToTheNameItAlreadyHas_IsFine()
    {
        var milk = await AddedAsync();

        var result = await Update().Handle(new UpdateProductCommand(_storeId, milk.Id, _userId, "Milk", "New text", 9m), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Update_ToTheNameOfAnotherLiveProduct_IsADuplicate_AndChangesNothing()
    {
        var milk = await AddedAsync("Milk");
        await AddedAsync("Bread");

        var result = await Update().Handle(new UpdateProductCommand(_storeId, milk.Id, _userId, "Bread", "x", 1m), CancellationToken.None);

        Assert.Same(ProductErrors.DuplicateName, result.Error);
        Assert.Equal("Milk", _products.Products.Single(product => product.Id == milk.Id).Name);
    }

    [Fact]
    public async Task Update_ByANonMember_IsToldTheStoreIsNotFound_AndChangesNothing()
    {
        var milk = await AddedAsync();

        var result = await Update().Handle(new UpdateProductCommand(_storeId, milk.Id, Guid.CreateVersion7(), "Hacked", "", 1m), CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
        Assert.Equal("Milk", Assert.Single(_products.Products).Name);
    }

    [Fact]
    public async Task Update_AnUnknownProduct_AnArchivedOne_AndAnotherStoresOne_AreNotFound()
    {
        var archived = new Product(Guid.CreateVersion7(), _storeId, "Old", "", 1m, DateTimeOffset.UtcNow);
        var foreign = new Product(Guid.CreateVersion7(), Guid.CreateVersion7(), "Foreign", "", 1m);
        _products.Products.AddRange([archived, foreign]);

        foreach (var productId in new[] { Guid.CreateVersion7(), archived.Id, foreign.Id })
        {
            var result = await Update().Handle(new UpdateProductCommand(_storeId, productId, _userId, "X", "", 1m), CancellationToken.None);

            Assert.Same(ProductErrors.NotFound, result.Error);
        }

        Assert.Equal("Foreign", _products.Products.Single(product => product.Id == foreign.Id).Name);
        Assert.True(_products.Products.Single(product => product.Id == archived.Id).IsArchived);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private ArchiveProductCommandHandler Archive() => new(new StoreAccess(_members), _products, new AdjustableTimeProvider(Now));

    [Fact]
    public async Task Archive_TakesTheProductOffSale_RecordsWhen_AndFreesItsName()
    {
        var milk = await AddedAsync();

        var result = await Archive().Handle(new ArchiveProductCommand(_storeId, milk.Id, _userId), CancellationToken.None);

        Assert.Equal(milk, result.Value);
        Assert.Equal(Now, Assert.Single(_products.Products).ArchivedAt);
        Assert.Same(ProductErrors.NotFound, (await new GetProductQueryHandler(_products).Handle(new GetProductQuery(_storeId, milk.Id), CancellationToken.None)).Error);
        Assert.True((await Create().Handle(Milk(), CancellationToken.None)).IsSuccess); // the name is free again
    }

    [Fact]
    public async Task Archive_ByANonMember_IsToldTheStoreIsNotFound_AndTheProductStaysOnSale()
    {
        var milk = await AddedAsync();

        var result = await Archive().Handle(new ArchiveProductCommand(_storeId, milk.Id, Guid.CreateVersion7()), CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
        Assert.False(Assert.Single(_products.Products).IsArchived);
    }

    [Fact]
    public async Task Archive_AnUnknownProduct_AnArchivedOne_AndAnotherStoresOne_AreNotFound()
    {
        var gone = await AddedAsync("Gone");
        await Archive().Handle(new ArchiveProductCommand(_storeId, gone.Id, _userId), CancellationToken.None);
        var foreign = new Product(Guid.CreateVersion7(), Guid.CreateVersion7(), "Foreign", "", 1m);
        _products.Products.Add(foreign);

        foreach (var productId in new[] { Guid.CreateVersion7(), gone.Id, foreign.Id })
        {
            var result = await Archive().Handle(new ArchiveProductCommand(_storeId, productId, _userId), CancellationToken.None);

            Assert.Same(ProductErrors.NotFound, result.Error);
        }

        Assert.False(_products.Products.Single(product => product.Id == foreign.Id).IsArchived);
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
